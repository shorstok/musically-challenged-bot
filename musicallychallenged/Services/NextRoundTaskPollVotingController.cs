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

        protected override string _votingStartedTemplate =>Loc.TaskSuggestionVotingStarted;
        protected override string _weHaveAWinnerTemplate => Loc.WeHaveAWinnerTaskSuggestion;
        protected override string _weHaveWinnersTemplate => Loc.WeHaveWinnersTaskSuggestion;

        public NextRoundTaskPollVotingController(ITelegramClient client,
            IBotConfiguration botConfiguration,
            IRepository repository,
            LocStrings loc,
            CrypticNameResolver crypticNameResolver,
            BroadcastController broadcastController,
            NextRoundTaskPollController pollController,
            TimeService timeService,
            Lazy<MidvoteEntryController> midvoteEntryController)
            : base(client, botConfiguration, repository, loc,
                  crypticNameResolver, broadcastController, midvoteEntryController, timeService)
        {
            _pollController = pollController;
        }

        protected override bool SetOrUpdateVote(User user, int voteVal, int entryId)
        {
            if (Repository.MaybeCreateVoteForAllActiveSuggestionsExcept(user, entryId, 0))
                logger.Info($"Set default suggestion vote value of 0 for user {user.GetUsernameOrNameWithCircumflex()} for all active entries except {entryId}");

            Repository.SetOrUpdateTaskPollVote(user, entryId, voteVal, out var updated);

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} {(updated ? "updated vote" : "voted")} {voteVal} for entry {entryId}");

            return updated;
        }

        protected override async Task<List<TaskSuggestion>> ConsolidateActiveVotes()
        {
            var activeSuggestions = Repository.
                CloseNextRoundTaskPollAndConsolidateVotes().
                OrderByDescending(v => v.ConsolidatedVoteCount ?? 0).
                ToList();

            //Remove voting controls from messages

            var votingResults = new StringBuilder();

            foreach (var entry in activeSuggestions)
            {
                await Client.EditMessageReplyMarkupAsync(
                    entry.ContainerChatId,
                    entry.ContainerMesssageId,
                    replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0]));

                var user = Repository.GetExistingUserWithTgId(entry.AuthorUserId);

                if (null == user)
                    continue;

                votingResults.AppendLine($"{user.GetHtmlUserLink()} : {entry.ConsolidatedVoteCount ?? 0}");
            }

            await BroadcastController.AnnounceInMainChannel(Loc.VotigResultsTemplate, false,
                Tuple.Create(LocTokens.Users, votingResults.ToString()));

            return activeSuggestions;
        }

        protected override IEnumerable<TaskSuggestion> GetActiveEntries() =>
            Repository.GetActiveTaskSuggestions();

        protected override IEnumerable<Tuple<TaskPollVote, User>> GetVotesForEntry(int entryId) =>
            Repository.GetVotesForTaskSuggestion(entryId);

        protected override TaskSuggestion GetExistingEntry(int entryId) =>
            Repository.GetExistingTaskSuggestion(entryId);

        protected override Instant ScheduleNextDeadline() =>
            Service.ScheduleNextDeadlineIn(Configuration.TaskSuggestionVotingDeadlineTimeHours);

        protected override string GetVoteDescriptionRealVotes(TaskPollVote vote) =>
            VotingSmiles[vote.Value];

        protected override string GetEntryText(User user, string votingDetails, string extra) =>
            _pollController.GetTaskSuggestionMessageText(
                user, 
                votingDetails, 
                ContestController.EscapeTgHtml(extra));

        protected override Task OnWinnerChosen(User winner, TaskSuggestion winningEntry)
        {
            Repository.UpdateState(s => s.CurrentTaskTemplate, winningEntry.Description);
            Repository.SetNextRoundTaskPollWinner(winner.Id);
            
            return Task.CompletedTask;
        }

        protected override bool IsValidStateToProduceAVotingWinner(int voteCount, int entriesCount) => 
            voteCount >= Configuration.MinAllowedVoteCountForWinners || entriesCount == 1;
    }
}
