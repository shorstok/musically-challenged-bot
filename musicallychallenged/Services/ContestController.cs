using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Events;
using musicallychallenged.Services.Telegram;
using Newtonsoft.Json.Converters;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public sealed class ContestController  : IDisposable
    {
        private readonly BroadcastController _broadcastController;
        private readonly TimeService _timeService;
        private readonly LocStrings _loc;
        private readonly IEventAggregator _aggregator;
        private readonly ITelegramClient _client;
        private readonly IBotConfiguration _configuration;
        private readonly PostponeService _postponeService;
        private readonly IRepository _repository;

        private static readonly ILog logger = Log.Get(typeof(ContestController));

        private ISubscription[] _subscriptions;

        public ContestController(BroadcastController broadcastController, 
            TimeService timeService,
            LocStrings loc,
            IEventAggregator aggregator,
            ITelegramClient client,
            IBotConfiguration configuration,
            PostponeService postponeService,
            IRepository repository)
        {
            _broadcastController = broadcastController;
            _timeService = timeService;
            _loc = loc;
            _aggregator = aggregator;
            _client = client;
            _configuration = configuration;
            _postponeService = postponeService;
            _repository = repository;

            _subscriptions = new ISubscription[]
            {
                _aggregator.Subscribe<MessageDeletedEvent>(OnMessageDeleted),
                _aggregator.Subscribe<BotBlockedEvent>(OnBotBlocked)
            };
        }

        private void OnBotBlocked(BotBlockedEvent obj)
        {
            if(null == obj.ChatId)
                return;

            _repository.DeleteUserWithPrivateChatId(obj.ChatId?.Identifier);
        }

        private void OnMessageDeleted(MessageDeletedEvent obj)
        {
            var deletedEntry = _repository.
                GetActiveContestEntries().
                ToArray().
                FirstOrDefault(e =>
                    e.ContainerMesssageId == obj.MessageId && 
                    e.ContainerChatId == obj.ChatId?.Identifier);

            if(null == deletedEntry)
                return;

            logger.Info($"Detected that containing message was deleted for entry {deletedEntry.Id}, deleting entry itself");

            _repository.DeleteContestEntry(deletedEntry.Id);
        }

        private readonly SemaphoreSlim _messageSemaphoreSlim = new SemaphoreSlim(1,1);

        public string GetContestEntryText(User user, string voteDetails, string extra)
        {
            StringBuilder detailsBuilder= new StringBuilder();

            if (!string.IsNullOrWhiteSpace(extra))
            {
                detailsBuilder.AppendLine();
                detailsBuilder.AppendLine(extra);
            }

            if (!string.IsNullOrWhiteSpace(voteDetails))
                detailsBuilder.AppendLine(voteDetails);

            return LocTokens.SubstituteTokens(_loc.Contest_FreshEntryTemplate,
                Tuple.Create(LocTokens.User, user.GetHtmlUserLink()),
                Tuple.Create(LocTokens.Details,detailsBuilder.ToString()));
        }

        public async Task UpdateContestEntry(User author, ActiveContestEntry entry)
        {
            await _client.EditMessageTextAsync(entry.ContainerChatId, entry.ContainerMesssageId,
                GetContestEntryText(author,String.Empty, entry.Description),ParseMode.Html);
        }

        public async Task SubmitNewEntry(Message response, User user)
        {
            await _messageSemaphoreSlim.WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(25)).Token).ConfigureAwait(false);
            
            try
            {
                var state = _repository.GetOrCreateCurrentState();

                if(state.State != ContestState.Contest)
                    return;

                if (state.VotingChannelId == null)
                {
                    logger.Error($"Voting channel {state.VotingChannelId} not set! Nowhere to forward");
                    return;
                }
                
                var forwared = await _client.ForwardMessageAsync(state.VotingChannelId.Value, response.Chat.Id, response.MessageId);

                if(null == forwared)
                    throw new InvalidOperationException($"Could not forward {user.GetUsernameOrNameWithCircumflex()} entry to voting channel {state.VotingChannelId}");

                var container = await _client.SendTextMessageAsync(forwared.Chat.Id, GetContestEntryText(user,String.Empty, string.Empty),
                    ParseMode.Html);

                if(null == container)
                    throw new InvalidOperationException($"Could not send {user.GetUsernameOrNameWithCircumflex()} entry to voting channel {state.VotingChannelId}");

                _repository.GetOrCreateContestEntry(user, forwared.Chat.Id, forwared.MessageId, container.MessageId, state.CurrentChallengeRoundNumber,out var previous);

                if (previous != null)
                {
                    await _client.DeleteMessageAsync(previous.ContainerChatId, previous.ContainerMesssageId);
                    await _client.DeleteMessageAsync(previous.ContainerChatId, previous.ForwardedPayloadMessageId);
                }
            }
            finally
            {
                _messageSemaphoreSlim.Release();
            }
        }


        public async Task WarnAboutContestDeadlineSoon(bool isFinal = true)
        {
            var active = _repository.GetActiveContestEntries().ToArray();

            var timeLeft = _timeService.FormatTimeLeftTillDeadline();
            
            var template = isFinal
                ? _loc.ContestDeadline_EnoughEntriesTemplateFinal
                : _loc.ContestDeadline_EnoughEntriesTemplateIntermediate;

            if(active.Length < _configuration.MinAllowedContestEntriesToStartVoting)
                template = isFinal
                    ? _loc.ContestDeadline_NotEnoughEntriesTemplateFinal
                    : _loc.ContestDeadline_NotEnoughEntriesTemplateIntermediate;

            var announcement = LocTokens.SubstituteTokens(template,
                Tuple.Create(LocTokens.Time, timeLeft),
                Tuple.Create(LocTokens.Details, active.Length.ToString()));

            await _broadcastController.AnnounceInMainChannel(announcement, false);            
        }

        public static string EscapeTgHtml(string source)
        {
            if(string.IsNullOrWhiteSpace(source))
                return String.Empty;

            return source.
                Replace("&", "&amp;").
                Replace("<", "&lt;").
                Replace(">", "&gt;").
                Replace("\"", "&quot;");
        }

        public string MaterializeTaskUsingCurrentTemplate()
        {            
            var state = _repository.GetOrCreateCurrentState();

            int? winnerId = state.CurrentTaskKind == SelectedTaskKind.Poll ? _repository.GetLastTaskPollWinner() : state.CurrentWinnerId;
            var winner = winnerId != null ? _repository.GetExistingUserWithTgId(state.CurrentWinnerId.Value) : null;

            var deadlineText = _timeService.FormatDateAndTimeToAnnouncementTimezone(state.NextDeadlineUTC);

            return LocTokens.SubstituteTokens(_loc.ContestStartMessageTemplateForMainChannelPin,
                Tuple.Create(LocTokens.User,winner?.GetHtmlUserLink()?? _loc.AnonymousAuthor) ,
                Tuple.Create(LocTokens.TaskDescription,EscapeTgHtml(state.CurrentTaskTemplate)),
                Tuple.Create(LocTokens.Deadline,deadlineText),
                Tuple.Create(LocTokens.VotingChannelLink,_configuration.VotingChannelInviteLink));
        }

        public Task KickstartContestAsync(string responseText, User user)
        {
            var votes = _repository.ConsolidateVotesForActiveEntriesGetAffected();

            if (votes.Any())
            {
                var state = _repository.GetOrCreateCurrentState();
                _repository.UpdateState(s => s.CurrentChallengeRoundNumber,state.CurrentChallengeRoundNumber+1);
            }

            _repository.SetCurrentTask(SelectedTaskKind.Manual, responseText);
            _repository.UpdateState(s => s.CurrentWinnerId,user.Id);

            _aggregator.Publish(new KickstartContestEvent());

            return Task.CompletedTask;
        }

        /// <summary>
        /// Create task from chosen template
        /// Announce contest start
        /// </summary>
        /// <returns></returns>
        public async Task InitiateContestAsync()
        {
            logger.Info($"Initiating contest");

            var state = _repository.GetOrCreateCurrentState();
            _timeService.ScheduleNextDeadlineIn(state.ContestDurationDays ?? 14, 22);

            //Pin task in main channel
            
            var pin = await _broadcastController.AnnounceInMainChannel(MaterializeTaskUsingCurrentTemplate(), true);
            await _broadcastController.AnnounceInVotingChannel(LocTokens.SubstituteTokens(
                _loc.ContestStartMessageTemplateForVotingChannel,
                Tuple.Create(LocTokens.Details,state.CurrentChallengeRoundNumber.ToString()),
                Tuple.Create(LocTokens.TaskDescription,EscapeTgHtml(state.CurrentTaskTemplate))),
                false);

            if (null == pin)
                throw new Exception("Invalid bot configuration -- couldnt post contest message");

            _repository.UpdateState(x=>x.CurrentTaskMessagelId, (int?)pin.MessageId);

            logger.Info($"Closing all unsatisfied postpone requests...");

            await _postponeService.CloseAllPostponeRequests(PostponeRequestState.ClosedDiscarded);
        }

        public async Task UpdateCurrentTaskMessage()
        {
            var state = _repository.GetOrCreateCurrentState();

            if(state.State != ContestState.Contest)
                return;

            if (state.MainChannelId == null)
            {
                logger.Error($"state.MainChannelId == null - cant updte current task");
                return;
            }

            if (state.CurrentTaskMessagelId == null)
            {
                logger.Error($"state.CurrentTaskMessagelId == null - cant updte current task");
                return;
            }

            try
            {
                await _client.EditMessageTextAsync(
                    state.MainChannelId.Value,
                    state.CurrentTaskMessagelId.Value,
                    MaterializeTaskUsingCurrentTemplate(),
                    ParseMode.Html);
            }
            catch (Exception e)
            {
                logger.Error($"UpdateCurrentTaskMessage:EditMessageTextAsync exception",e);
            }
        }


        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        }
    }
}
