using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public class NextRoundTaskPollController
    {
        private readonly IRepository _repository;
        private readonly IBotConfiguration _botConfiguration;
        private readonly LocStrings _loc;
        private readonly ITelegramClient _client;

        private static readonly ILog logger = Log.Get(typeof(NextRoundTaskPollController));

        public NextRoundTaskPollController(IRepository repository, IBotConfiguration botConfiguration, 
            LocStrings loc, ITelegramClient client)
        {
            _repository = repository;
            _botConfiguration = botConfiguration;
            _loc = loc;
            _client = client;
        }

        private readonly SemaphoreSlim _messageSemaphoreSlim = new SemaphoreSlim(1, 1);

        public async Task StartTaskPoll()
        {
            throw new NotImplementedException();
        }

        public async Task FinishTaskPoll()
        {
            throw new NotImplementedException();
        }

        public string GetTaskSuggestionMessageText(User user, string voteDetails, string description)
        {
            StringBuilder detailsBuilder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(description))
            {
                detailsBuilder.AppendLine();
                detailsBuilder.AppendLine(description);
            }

            if (!string.IsNullOrWhiteSpace(voteDetails))
                detailsBuilder.AppendLine(voteDetails);

            return LocTokens.SubstituteTokens(_loc.NextRoundTaskPollController_SuggestionTemplate,
                Tuple.Create(LocTokens.User, user.GetHtmlUserLink()),
                Tuple.Create(LocTokens.Details, detailsBuilder.ToString()));
        }

        public async Task SaveTaskSuggestion(string description, User user)
        {
            await _messageSemaphoreSlim.WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(25)).Token).ConfigureAwait(false);

            try
            {
                var state = _repository.GetOrCreateCurrentState();

                if (state.VotingChannelId == null)
                {
                    logger.Error($"Voting channel not set! Nowhere to forward!");
                    return;
                }

                var container = await _client.SendTextMessageAsync(state.VotingChannelId.Value, 
                    GetTaskSuggestionMessageText(user, string.Empty, description), ParseMode.Html);

                if (container == null)
                    throw new InvalidOperationException($"Could not send {user.GetUsernameOrNameWithCircumflex()} suggestion to voting channel {state.VotingChannelId}");

                _repository.CreateOrUpdateTaskSuggestion(user, description,
                    container.Chat.Id, container.MessageId, out var previous);

                if (previous != null)
                {
                    await _client.DeleteMessageAsync(previous.ContainerChatId, previous.ContainerMesssageId);
                }
            }
            finally
            {
                _messageSemaphoreSlim.Release();
            }
        }
    }
}
