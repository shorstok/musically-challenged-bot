using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public sealed class MidvoteEntryController
    {
        private readonly IRepository _repository;
        private readonly VotingController _votingController;
        private readonly ITelegramClient _client;
        private readonly ContestController _contestController;
        private static readonly ILog logger = Log.Get(typeof(MidvoteEntryController));

        private readonly ConcurrentDictionary<string, object> _activeMidvotePins =
            new ConcurrentDictionary<string, object>();
        
        private readonly SemaphoreSlim _messageSemaphoreSlim = new SemaphoreSlim(1,1);


        public MidvoteEntryController(IRepository repository,
            VotingController votingController,
            ITelegramClient client,
            ContestController contestController)
        {
            _repository = repository;
            _votingController = votingController;
            _client = client;
            _contestController = contestController;
        }

        public Task<int> CreateMidvotePin(string pin)
        {
            if (pin == null) throw new ArgumentNullException(nameof(pin));
           
            _activeMidvotePins.TryAdd(pin.ToLowerInvariant().Trim(), null);
            
            return Task.FromResult(_activeMidvotePins.Count);
        }

        public async Task SubmitNewEntry(Message message, User author)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (author == null) throw new ArgumentNullException(nameof(author));
            
            await _messageSemaphoreSlim
                .WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(25)).Token)
                .ConfigureAwait(false);

            try
            {
                logger.Info($"About to submit entry mid-vote");

                if (_repository.GetActiveContestEntryForUser(author.Id) != null)
                {
                    logger.Info($"Active entry already exists - no midvote submissions allowed");
                    return;
                }
                
                var state = _repository.GetOrCreateCurrentState();

                if (state.VotingChannelId == null)
                {
                    logger.Error($"Voting channel {state.VotingChannelId} not set! Nowhere to forward");
                    return;
                }
                
                var forwared = await _client.ForwardMessageAsync(state.VotingChannelId.Value, message.Chat.Id, message.MessageId);

                if(null == forwared)
                    throw new InvalidOperationException($"Could not forward {author.GetUsernameOrNameWithCircumflex()} entry to voting channel {state.VotingChannelId}");

                var container = await _client.SendTextMessageAsync(forwared.Chat.Id, 
                    _contestController.GetContestEntryText(author,String.Empty, string.Empty),
                    ParseMode.Html);
                

                if(null == container)
                    throw new InvalidOperationException($"Could not send {author.GetUsernameOrNameWithCircumflex()} entry to voting channel {state.VotingChannelId}");

                _repository.GetOrCreateContestEntry(author, forwared.Chat.Id, forwared.MessageId, container.MessageId, state.CurrentChallengeRoundNumber,out var previous);

                if (previous != null)
                {
                    await _client.DeleteMessageAsync(previous.ContainerChatId, previous.ContainerMesssageId);
                    await _client.DeleteMessageAsync(previous.ContainerChatId, previous.ForwardedPayloadMessageId);
                }

                var entry = _repository.GetActiveContestEntryForUser(author.Id);

                await _votingController.CreateVotingControlsForEntry(entry);
            }
            finally
            {
                _messageSemaphoreSlim.Release();
            }
        }

        public Task<bool> IsAvailable(User user)
        {
            if (_repository.GetOrCreateCurrentState().State != ContestState.Voting)
            {
                logger.Info($"Not in votring state");
                return Task.FromResult(false);
            }

            if (!_activeMidvotePins.Any())
            {
                logger.Info($"No active midvote pins");
                return Task.FromResult(false);
            }

            if (_repository.GetActiveContestEntryForUser(user.Id) != null)
            {
                logger.Info($"User {user?.GetUsernameOrNameWithCircumflex()} already has active contest entry");
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        public Task<bool> ValidatePin(Message message)
        {
            var pin = message?.Text?.ToLowerInvariant().Trim() ?? String.Empty;

            return Task.FromResult(_activeMidvotePins.TryRemove(pin, out _));
        }
    }
}