using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Sync;
using musicallychallenged.Services.Sync.DTO;
using musicallychallenged.Services.Telegram;
using NodaTime;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public class VotingController : VotingControllerBase<ActiveContestEntry, Vote>
    {
        private static readonly ILog logger = Log.Get(typeof(VotingController));

        private readonly ContestController _contestController;
        private readonly SyncService _syncService;

        public override string Prefix { get; } = "v";

        public override Dictionary<int, string> VotingSmiles { get; } = new Dictionary<int, string>
        {
            { 1, "🌑" }, {2, "🌘" }, {3, "🌗" }, {4, "🌖" }, {5, "🌕" }
        };

        protected override string _votingStartedTemplate => 
            Loc.VotingStarted;

        protected override string _weHaveAWinnerTemplate =>
            Loc.WeHaveAWinner;

        protected override string _weHaveWinnersTemplate =>
            Loc.WeHaveWinners;

        public VotingController(ITelegramClient client,
            IBotConfiguration botConfiguration,
            IRepository repository,
            LocStrings loc,
            CrypticNameResolver crypticNameResolver,
            BroadcastController broadcastController,
            ContestController contestController,
            TimeService timeService,
            SyncService syncService,
            Lazy<MidvoteEntryController> midvoteEntryController) 
            : base(client, botConfiguration, repository, loc, 
                  crypticNameResolver, broadcastController,midvoteEntryController, timeService)
        {
            _contestController = contestController;
            _syncService = syncService;
        }

        protected override bool SetOrUpdateVote(User user, int voteVal, int entryId)
        {
            var defaultVoteValue = GetDefaultVoteForUser(user);

            if (Repository.MaybeCreateVoteForAllActiveEntriesExcept(user, entryId, defaultVoteValue))
                logger.Info($"Set default vote value of {defaultVoteValue} for user {user.GetUsernameOrNameWithCircumflex()} for all active entries except {entryId}");

            Repository.SetOrUpdateVote(user, entryId, voteVal, out var updated);

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} {(updated ? "updated vote" : "voted")} {voteVal} for entry {entryId}");

            return updated;
        }

        public int GetDefaultVoteForUser(User user)
        {
            double? average = Repository.GetAverageVoteForUser(user);

            return (int)Math.Round(average ?? Configuration.MinVoteValue * 0.5 + Configuration.MaxVoteValue * 0.5);
        }

        /// <summary>
        /// Maybe post message ~ 'voting about to end' in main channel and 
        /// maybe warn ~top contesters, to prepare them for new round's task selection
        /// </summary>
        /// <returns></returns>
        public Task WarnAboutVotingDeadlineSoon()
        {
            //todo : warn probable winners
            return Task.CompletedTask;
        }

        public async Task UpdateCurrentTaskMessage()
        {
            var state = Repository.GetOrCreateCurrentState();

            if (state.State != ContestState.Voting)
                return;

            if (state.MainChannelId == null)
            {
                logger.Error($"state.MainChannelId == null - cant updte current voting deadline");
                return;
            }

            if (state.CurrentVotingDeadlineMessageId == null)
            {
                logger.Error($"state.CurrentVotingDeadlineMessageId == null - cant updte current voting deadline");
                return;
            }

            try
            {
                await Client.EditMessageTextAsync(
                    state.MainChannelId.Value,
                    state.CurrentVotingDeadlineMessageId.Value,
                    GetVotingStartedMessage(state),
                    ParseMode.Html);
            }
            catch (Exception e)
            {
                logger.Error($"UpdateCurrentTaskMessage:EditMessageTextAsync exception", e);
            }
        }

        protected override IEnumerable<ActiveContestEntry> GetActiveEntries() =>
            Repository.GetActiveContestEntries();

        protected override IEnumerable<Tuple<Vote, User>> GetVotesForEntry(int entryId) =>
            Repository.GetVotesForEntry(entryId);

        protected override ActiveContestEntry GetExistingEntry(int entryId) =>
            Repository.GetExistingEntry(entryId);

        protected override Instant ScheduleNextDeadline() =>
            Service.ScheduleNextDeadlineIn(Repository.GetOrCreateCurrentState().VotingDurationDays ?? 2, 22);

        protected async override Task<List<ActiveContestEntry>> ConsolidateActiveVotes()
        {
            var activeEntries = Repository.
                ConsolidateVotesForActiveEntriesGetAffected().
                OrderByDescending(v => v.ConsolidatedVoteCount ?? 0).
                ToList();

            //Remove voting controls from messages

            var votingResults = new StringBuilder();

            foreach (var entry in activeEntries)
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

            await _syncService.UpdateVotes(activeEntries);

            await BroadcastController.AnnounceInMainChannel(Loc.VotigResultsTemplate, false,
                Tuple.Create(LocTokens.Users, votingResults.ToString()));

            return activeEntries;
        }

        protected override async Task OnVotingStartedAsync()
        {
            var state = Repository.GetOrCreateCurrentState();
            
            await _syncService.PatchRoundInfo(
                state.CurrentChallengeRoundNumber, 
                null,
                null,
                BotContestRoundState.Voting);
        }

        protected override string GetVoteDescriptionRealVotes(Vote vote) =>
            string.Join("", Enumerable.Repeat(VotingSmiles[vote.Value], vote.Value));

        protected override string GetEntryText(User user, string votingDetails, string extra) =>
            _contestController.GetContestEntryText(user, votingDetails, extra);

        protected override async Task OnWinnerChosen(User winner, ActiveContestEntry winningEntry)
        {
            //forward winner's entry to main channel
            var state = Repository.GetOrCreateCurrentState();

            try
            {
                if (state.MainChannelId != null)
                    await Client.ForwardMessageAsync(state.MainChannelId.Value, winningEntry.ContainerChatId,
                        winningEntry.ForwardedPayloadMessageId);
                else
                    logger.Warn(
                        $"{nameof(SystemState)}.{nameof(SystemState.MainChannelId)} not set (null), skipped winning entry forward");

            }
            catch (BadRequestException e)
            {
                logger.Warn($"Got {e.GetType().Name} when trying to forward message ({e.Message})");
            }

            //persist winner Id

            Repository.UpdateState(s => s.CurrentWinnerId, winner.Id);
        }

        protected override bool IsValidStateToProduceAVotingWinner(int voteCount, int entriesCount) =>
            voteCount >= Configuration.MinAllowedVoteCountForWinners;

        protected override async Task OnEnteredFinalization()
        {
            var state = Repository.GetOrCreateCurrentState();

            await _syncService.UpdateRoundState(state.CurrentChallengeRoundNumber, BotContestRoundState.Closed);
            
            Repository.UpdateState(x => x.CurrentChallengeRoundNumber, state.CurrentChallengeRoundNumber + 1);
            logger.Info($"Challenge round number set to {state.CurrentChallengeRoundNumber}");
        }

        protected override List<ActiveContestEntry> _filterConsolidatedEntriesIfEnoughContester(List<ActiveContestEntry> entries)
        {
            var state = Repository.GetOrCreateCurrentState();
            entries.RemoveAll(e => e.AuthorUserId == state.CurrentWinnerId);
            return entries;
        }
    }
}