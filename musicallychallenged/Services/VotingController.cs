using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
using NUnit.Framework.Internal.Commands;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public class VotingController : VotingControllerBase<ActiveContestEntry, Vote>
    {
        private static readonly ILog logger = Log.Get(typeof(VotingController));

        private readonly ContestController _contestController;

        public override string Prefix { get; } = "v";

        public override Dictionary<int, string> VotingSmiles { get; } = new Dictionary<int, string>
        {
            { 1, "🌑" }, {2, "🌘" }, {3, "🌗" }, {4, "🌖" }, {5, "🌕" }
        };

        protected override string _votingStartedTemplate { get; }
        protected override string _weHaveAWinnerTemplate { get; }
        protected override string _weHaveWinnersTemplate { get; }

        public VotingController(
            ITelegramClient client,
            IBotConfiguration botConfiguration,
            IRepository repository,
            LocStrings loc,
            CrypticNameResolver crypticNameResolver,
            BroadcastController broadcastController,
            ContestController contestController,
            TimeService timeService) 
            : base(client, botConfiguration, repository, loc, 
                  crypticNameResolver, broadcastController, timeService)
        {
            _contestController = contestController;

            _votingStartedTemplate = _loc.VotingStarted;
            _weHaveAWinnerTemplate = _loc.WeHaveAWinner;
            _weHaveWinnersTemplate = _loc.WeHaveWinners;
        }

        protected override bool SetOrUpdateVote(User user, int voteVal, int entryId)
        {
            var defaultVoteValue = GetDefaultVoteForUser(user);

            if (_repository.MaybeCreateVoteForAllActiveEntriesExcept(user, entryId, defaultVoteValue))
                logger.Info($"Set default vote value of {defaultVoteValue} for user {user.GetUsernameOrNameWithCircumflex()} for all active entries except {entryId}");

            _repository.SetOrUpdateVote(user, entryId, voteVal, out var updated);

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} {(updated ? "updated vote" : "voted")} {voteVal} for entry {entryId}");

            return updated;
        }

        public int GetDefaultVoteForUser(User user)
        {
            double? average = _repository.GetAverageVoteForUser(user);

            return (int)Math.Round(average ?? _botConfiguration.MinVoteValue * 0.5 + _botConfiguration.MaxVoteValue * 0.5);
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
            var state = _repository.GetOrCreateCurrentState();

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
                await _client.EditMessageTextAsync(
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
            _repository.GetActiveContestEntries();

        protected override IEnumerable<Tuple<Vote, User>> GetVotesForEntry(int entryId) =>
            _repository.GetVotesForEntry(entryId);

        protected override ActiveContestEntry GetExistingEntry(int entryId) =>
            _repository.GetExistingEntry(entryId);

        protected override Instant ScheduleNextDeadline() =>
            _timeService.ScheduleNextDeadlineIn(_repository.GetOrCreateCurrentState().VotingDurationDays ?? 2, 22);

        protected async override Task<List<ActiveContestEntry>> ConsolidateActiveVotes()
        {
            var activeEntries = _repository.
                ConsolidateVotesForActiveEntriesGetAffected().
                OrderByDescending(v => v.ConsolidatedVoteCount ?? 0).
                ToList();

            //Remove voting controls from messages

            var votingResults = new StringBuilder();

            foreach (var entry in activeEntries)
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

            return activeEntries;
        }

        protected override string GetVoteDescriptionRealVotes(Vote vote) =>
            string.Join("", Enumerable.Repeat(VotingSmiles[vote.Value], vote.Value));

        protected override string GetEntryText(User user, string votingDetails, string extra) =>
            _contestController.GetContestEntryText(user, votingDetails, extra);

        protected async override Task _onWinnerChosen(User winner, ActiveContestEntry winningEntry)
        {
            //forward winner's entry to main channel
            var state = _repository.GetOrCreateCurrentState();

            try
            {
                if (state.MainChannelId != null)
                    await _client.ForwardMessageAsync(state.MainChannelId.Value, winningEntry.ContainerChatId,
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

            _repository.UpdateState(s => s.CurrentWinnerId, winner.Id);
        }

        protected override Task _onEnteredFinalization()
        {
            var state = _repository.GetOrCreateCurrentState();
            _repository.UpdateState(x => x.CurrentChallengeRoundNumber, state.CurrentChallengeRoundNumber + 1);
            logger.Info($"Challenge round number set to {state.CurrentChallengeRoundNumber}");

            return Task.Run(() => { });
        }

        protected override List<ActiveContestEntry> _filterConsolidatedEntriesIfEnoughContester(List<ActiveContestEntry> entries)
        {
            var state = _repository.GetOrCreateCurrentState();
            entries.RemoveAll(e => e.AuthorUserId == state.CurrentWinnerId);
            return entries;
        }
    }
}