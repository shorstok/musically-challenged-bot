using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Helpers;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using NodaTime;
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
    public class NextRoundTaskPollVotingController : VotingControllerBase<TaskSuggestion, TaskPollVote>
    {
        private readonly NextRoundTaskPollController _pollController;

        private static readonly ILog logger = Log.Get(typeof(NextRoundTaskPollVotingController));

        public override string Prefix { get; } = "nv";

        public override Dictionary<int, string> VotingSmiles { get; } = new Dictionary<int, string>
        {
            { -1, "👎"}, {0, "🤷‍"}, {1, "👍"}
        };

        protected override string _votingStartedTemplate { get; }
        protected override string _weHaveAWinnerTemplate { get; }
        protected override string _weHaveWinnersTemplate { get; }

        public NextRoundTaskPollVotingController(
            ITelegramClient client,
            IBotConfiguration botConfiguration,
            IRepository repository,
            LocStrings loc,
            CrypticNameResolver crypticNameResolver,
            BroadcastController broadcastController,
            NextRoundTaskPollController pollController,
            TimeService timeService)
            : base(client, botConfiguration, repository, loc,
                  crypticNameResolver, broadcastController, timeService)
        {
            _pollController = pollController;

            _votingStartedTemplate = _loc.TaskSuggestionVotingStarted;
            _weHaveAWinnerTemplate = _loc.WeHaveAWinnerTaskSuggestion;
            _weHaveWinnersTemplate = _loc.WeHaveWinnersTaskSuggestion;
        }

        protected override bool SetOrUpdateVote(User user, int voteVal, int entryId)
        {
            if (_repository.MaybeCreateVoteForAllActiveSuggestionsExcept(user, entryId, 0))
                logger.Info($"Set default suggestion vote value of 0 for user {user.GetUsernameOrNameWithCircumflex()} for all active entries except {entryId}");

            _repository.SetOrUpdateTaskPollVote(user, entryId, voteVal, out var updated);

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} {(updated ? "updated vote" : "voted")} {voteVal} for entry {entryId}");

            return updated;
        }

        protected override async Task<List<TaskSuggestion>> ConsolidateActiveVotes()
        {
            var activeSuggestions = _repository.
                CloseNextRoundTaskPollAndConsolidateVotes().
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

        protected override IEnumerable<TaskSuggestion> GetActiveEntries() =>
            _repository.GetActiveTaskSuggestions();

        protected override IEnumerable<Tuple<TaskPollVote, User>> GetVotesForEntry(int entryId) =>
            _repository.GetVotesForTaskSuggestion(entryId);

        protected override TaskSuggestion GetExistingEntry(int entryId) =>
            _repository.GetExistingTaskSuggestion(entryId);

        protected override Instant ScheduleNextDeadline() =>
            _timeService.ScheduleNextDeadlineIn(_botConfiguration.TaskSuggestionVotingDeadlineTimeHours);

        protected override string GetVoteDescriptionRealVotes(TaskPollVote vote) =>
            VotingSmiles[vote.Value];

        protected override string GetEntryText(User user, string votingDetails, string extra) =>
            _pollController.GetTaskSuggestionMessageText(user, votingDetails, extra);

        protected override Task OnWinnerChosen(User winner, TaskSuggestion winningEntry)
        {
            _repository.UpdateState(s => s.CurrentTaskTemplate, winningEntry.Description);
            _repository.SetNextRoundTaskPollWinner(winner.Id);
            return Task.Run(() => { });
        }
    }
}
