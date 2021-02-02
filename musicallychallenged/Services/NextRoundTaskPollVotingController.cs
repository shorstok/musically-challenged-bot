using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Helpers;
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
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public class NextRoundTaskPollVotingController : ITelegramQueryHandler
    {
        private readonly IRepository _repository;
        private readonly IBotConfiguration _botConfiguration;
        private readonly TimeService _timeService;
        private readonly LocStrings _loc;
        private readonly NextRoundTaskPollController _pollController;
        private readonly BroadcastController _broadcastController;
        private readonly ITelegramClient _client;
        private readonly VotingControllerHelper<TaskSuggestion, TaskPollVote> _helper;

        public NextRoundTaskPollVotingController(
            IRepository repository, 
            IBotConfiguration botConfiguration, 
            TimeService timeService, 
            LocStrings loc, 
            NextRoundTaskPollController pollController, 
            BroadcastController broadcastController, 
            ITelegramClient client,
            VotingControllerHelper<TaskSuggestion, TaskPollVote> helper)
        {
            _repository = repository;
            _botConfiguration = botConfiguration;
            _timeService = timeService;
            _loc = loc;
            _pollController = pollController;
            _broadcastController = broadcastController;
            _client = client;
            _helper = helper;

            _helper.SetController(this);

            _helper.ConfigureDbInteraction(
                SetOrUpdateVote,
                _repository.GetActiveTaskSuggestions,
                _repository.GetVotesForTaskSuggestion,
                _repository.GetExistingTaskSuggestion,
                () => _timeService.ScheduleNextDeadlineIn(_botConfiguration.TaskSuggestionVotingDeadlineTimeHours),
                ConsolidateActiveVotes);

            _helper.ConfigureMessageTemplates(
                _votingSmiles,
                v => _votingSmiles[v.Value],
                _pollController.GetTaskSuggestionMessageText,
                GetVotingStartedMessage,
                _loc.WeHaveAWinnerTaskSuggestion,
                _loc.WeHaveWinnersTaskSuggestion);

            _helper.ConfigureVotingFinalization(
                (u, ts) => _repository.UpdateState(s => s.CurrentTaskTemplate, ts.Description));
        }

        public bool? SetOrUpdateVote(User user, int voteVal, int entryId)
        {
            if (_repository.MaybeCreateVoteForAllActiveSuggestionsExcept(user, entryId, 0))
                logger.Info($"Set default suggestion vote value of 0 for user {user.GetUsernameOrNameWithCircumflex()} for all active entries except {entryId}");

            _repository.SetOrUpdateTaskPollVote(user, entryId, voteVal, out var updated);

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} {(updated ? "updated vote" : "voted")} {voteVal} for entry {entryId}");

            return updated;
        }

        private static readonly ILog logger = Log.Get(typeof(NextRoundTaskPollVotingController));
        public string Prefix { get; } = "nv";

        public async Task ExecuteQuery(CallbackQuery callbackQuery) =>
            await _helper.ExecuteQuery(callbackQuery);

        public async Task StartVotingAsync() =>
            await _helper.StartVotingAsync();

        private string GetVotingStartedMessage(SystemState state)
        {
            var deadlineText = _timeService.FormatDateAndTimeToAnnouncementTimezone(state.NextDeadlineUTC);

            return LocTokens.SubstituteTokens(_loc.TaskSuggestionVotingStarted,
                Tuple.Create(LocTokens.VotingChannelLink, _botConfiguration.VotingChannelInviteLink),
                Tuple.Create(LocTokens.Deadline, deadlineText));
        }

        public async Task<Tuple<VotingFinalizationResult, User>> FinalizeVoting() =>
            await _helper.FinalizeVoting();

        public async Task<List<TaskSuggestion>> ConsolidateActiveVotes()
        {
            var activeSuggestions = _repository.
                CloseNextRoundTaskPoll().
                OrderByDescending(v => v.ConsolidatedVoteCount ?? 0).
                ToList();

            //Remove voting controls from messages

            var votingResults = new StringBuilder();

            foreach (var entry in activeSuggestions)
            {
                await _client.EditMessageReplyMarkupAsync(
                    entry.ContainerChatId,
                    entry.ContainerMesssageId,
                    replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0]));

                var user = _repository.GetExistingUserWithTgId(entry.AuthorUserId);

                if (null == user)
                    continue;

                votingResults.AppendLine($"{user.GetHtmlUserLink()} : {entry.ConsolidatedVoteCount ?? 0}");
            }

            await _broadcastController.AnnounceInMainChannel(_loc.VotigResultsTemplate, false,
                Tuple.Create(LocTokens.Users, votingResults.ToString()));

            return activeSuggestions;
        }

        public static readonly Dictionary<int, string> _votingSmiles = new Dictionary<int, string>
        {
            { -1, "👎"}, {0, "🤷‍"}, {1, "👍"}
        };
    }
}
