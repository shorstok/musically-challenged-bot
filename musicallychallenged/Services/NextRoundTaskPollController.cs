using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Events;
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
        private readonly IBotConfiguration _configuration;
        private readonly LocStrings _loc;
        private readonly ITelegramClient _client;
        private readonly TimeService _timeService;
        private readonly ContestController _contestController;
        private readonly BroadcastController _broadcastController;
        private readonly IEventAggregator _aggregator;

        private ISubscription[] _subscriptions;

        private static readonly ILog logger = Log.Get(typeof(NextRoundTaskPollController));

        public NextRoundTaskPollController(
            IRepository repository,
            IBotConfiguration configuration,
            LocStrings loc,
            ITelegramClient client,
            TimeService timeService,
            ContestController contestController,
            BroadcastController broadcastController,
            IEventAggregator eventAggregator)
        {
            _repository = repository;
            _configuration = configuration;
            _loc = loc;
            _client = client;
            _timeService = timeService;
            _contestController = contestController;
            _broadcastController = broadcastController;
            _aggregator = eventAggregator;

            _subscriptions = new ISubscription[]
            {
                eventAggregator.Subscribe<MessageDeletedEvent>(OnMessageDeleted)
            };
        }

        private readonly SemaphoreSlim _messageSemaphoreSlim = new SemaphoreSlim(1, 1);

        private void OnMessageDeleted(MessageDeletedEvent obj)
        {
            var state = _repository.GetOrCreateCurrentState();

            //Short-circuit event with (cached) state - if message deleted was not
            //in voting chat, no need to cross-reference IDs with database

            if (state.VotingChannelId != obj.ChatId?.Identifier)
                return;

            var deletedEntry = _repository
                .GetActiveTaskSuggestions()
                .FirstOrDefault(s => s.ContainerMesssageId == obj.MessageId && 
                                     s.ContainerChatId == obj.ChatId?.Identifier);

            if (deletedEntry == null)
                return;

            logger.Info($"Detected that containing message was deleted for entry {deletedEntry.Id}, deleting entry itself");

            _repository.DeleteTaskSuggestion(deletedEntry.Id);
        }

        public async Task StartTaskPollAsync()
        {
            logger.Info("Initiating NextRoundTaskPoll");

            _repository.UpdateState(s => s.CurrentTaskKind, SelectedTaskKind.Poll);

            var state = _repository.GetOrCreateCurrentState();
            _repository.CreateNextRoundTaskPoll();
            var deadline = _timeService.ScheduleNextDeadlineIn(_configuration.TaskSuggestionCollectionDeadlineTimeHours);
            var deadlineText = _timeService.FormatDateAndTimeToAnnouncementTimezone(deadline);

            var previousWinner = state.CurrentWinnerId.HasValue ? 
                _repository.GetExistingUserWithTgId(state.CurrentWinnerId.Value) : null;

            // pin an announcement in the the main channel

            var pin = await _broadcastController.AnnounceInMainChannel(LocTokens.SubstituteTokens(
                _loc.NextRoundTaskPollController_AnnouncementTemplateMainChannel,
                Tuple.Create(LocTokens.User, previousWinner?.GetUsernameOrNameWithCircumflex()??"SOMEBODY"),
                Tuple.Create(LocTokens.Deadline, deadlineText),
                Tuple.Create(LocTokens.RulesUrl, _configuration.RulesURL),
                Tuple.Create(LocTokens.VotingChannelLink, _configuration.VotingChannelInviteLink)),
                true);
            await _broadcastController.AnnounceInVotingChannel(LocTokens.SubstituteTokens(
                _loc.NextRoundTaskPollController_AnnouncementTemplateVotingChannel),
                false);

            if (null == pin)
                throw new Exception("Invalid bot configuration -- couldn't post contest message");
        }

        public void HaltTaskPoll()
        {
            _repository.CloseNextRoundTaskPollAndConsolidateVotes();
        }

        public string GetTaskSuggestionMessageText(User user, string voteDetails, string descriptionEscaped)
        {
            StringBuilder detailsBuilder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(descriptionEscaped))
            {
                detailsBuilder.AppendLine();
                detailsBuilder.AppendLine(descriptionEscaped);
            }

            if (!string.IsNullOrWhiteSpace(voteDetails))
                detailsBuilder.AppendLine(voteDetails);

            return LocTokens.SubstituteTokens(_loc.NextRoundTaskPollController_SuggestionTemplate,
                Tuple.Create(LocTokens.User, user.GetHtmlUserLink()),
                Tuple.Create(LocTokens.Details, detailsBuilder.ToString()));
        }

        public async Task SaveTaskSuggestion(string descriptionUnescaped, User user)
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

                var container = await _client.SendTextMessageAsync(
                    state.VotingChannelId.Value, 
                    GetTaskSuggestionMessageText(user, string.Empty, ContestController.EscapeTgHtml(descriptionUnescaped)),
                    ParseMode.Html);

                if (container == null)
                    throw new InvalidOperationException($"Could not send {user.GetUsernameOrNameWithCircumflex()} suggestion to voting channel {state.VotingChannelId}");

                _repository.CreateOrUpdateTaskSuggestion(user, descriptionUnescaped,
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

        public Task KickstartTaskPollAsync( User user)
        {
            _contestController.IsolatePreviousRoundTasks();
            
            _aggregator.Publish(new KickstartNextRoundTaskPollEvent());

            return Task.CompletedTask;
        }
    }
}
