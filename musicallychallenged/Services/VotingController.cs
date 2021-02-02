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
using NUnit.Framework.Internal.Commands;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public class VotingController : ITelegramQueryHandler
    {
        private static readonly ILog logger = Log.Get(typeof(VotingController));

        private readonly IRepository _repository;
        private readonly IBotConfiguration _botConfiguration;
        private readonly TimeService _timeService;
        private readonly LocStrings _loc;
        private readonly CrypticNameResolver _crypticNameResolver;
        private readonly ContestController _contestController;
        private readonly BroadcastController _broadcastController;
        private readonly ITelegramClient _client;
        private readonly VotingControllerHelper<ActiveContestEntry, Vote> _helper;

        public string Prefix { get; } = "v";

        public VotingController(IRepository repository,
            IBotConfiguration botConfiguration,
            TimeService timeService,
            LocStrings loc,
            CrypticNameResolver crypticNameResolver,
            ContestController contestController,
            BroadcastController broadcastController,
            ITelegramClient client,
            VotingControllerHelper<ActiveContestEntry, Vote> helper)
        {
            _repository = repository;
            _botConfiguration = botConfiguration;
            _timeService = timeService;
            _loc = loc;
            _crypticNameResolver = crypticNameResolver;
            _contestController = contestController;
            _broadcastController = broadcastController;
            _client = client;
            _helper = helper;

            _helper.SetController(this);

            _helper.ConfigureDbInteraction(
                SetOrUpdateVote, 
                _repository.GetActiveContestEntries,
                _repository.GetVotesForEntry,
                _repository.GetExistingEntry,
                () => _timeService.ScheduleNextDeadlineIn(
                    _repository.GetOrCreateCurrentState().VotingDurationDays ?? 2, 22),
                ConsolidateActiveVotes);

            _helper.ConfigureMessageTemplates(
                _votingSmiles,
                v => string.Join("", Enumerable.Repeat(_votingSmiles[v.Value], v.Value)),
                _contestController.GetContestEntryText,
                GetVotingStartedMessage,
                _loc.WeHaveAWinner,
                _loc.WeHaveWinners);

            _helper.ConfigureVotingFinalization(
                OnWinnerChosen,
                () => {
                    var state = _repository.GetOrCreateCurrentState();
                    _repository.UpdateState(x => x.CurrentChallengeRoundNumber, state.CurrentChallengeRoundNumber + 1);
                    logger.Info($"Challenge round number set to {state.CurrentChallengeRoundNumber}");
                }, entries => {
                    var state = _repository.GetOrCreateCurrentState();
                    entries.RemoveAll(e => e.AuthorUserId == state.CurrentWinnerId);
                    return entries;
                });
        }

        public bool? SetOrUpdateVote(User user, int voteVal, int entryId)
        {
            var defaultVoteValue = GetDefaultVoteForUser(user);

            if (_repository.MaybeCreateVoteForAllActiveEntriesExcept(user, entryId, defaultVoteValue))
                logger.Info($"Set default vote value of {defaultVoteValue} for user {user.GetUsernameOrNameWithCircumflex()} for all active entries except {entryId}");

            _repository.SetOrUpdateVote(user, entryId, voteVal, out var updated);

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} {(updated ? "updated vote" : "voted")} {voteVal} for entry {entryId}");

            return updated;
        }

        public async Task ExecuteQuery(CallbackQuery callbackQuery) =>
            await _helper.ExecuteQuery(callbackQuery);

        /// <summary>
        /// Create voting controls in voting channel, announce about voting start
        /// </summary>
        /// <returns></returns>
        public async Task StartVotingAsync() =>
            await _helper.StartVotingAsync();

        public async Task<Tuple<VotingFinalizationResult, User>> FinalizeVoting() =>
            await _helper.FinalizeVoting();

        public int GetDefaultVoteForUser(User user)
        {
            double? average = _repository.GetAverageVoteForUser(user);

            return (int)Math.Round(average ?? _botConfiguration.MinVoteValue * 0.5 + _botConfiguration.MaxVoteValue * 0.5);
        }

        // voteValue -> emoji
        public static readonly Dictionary<int, string> _votingSmiles = new Dictionary<int, string>
        {
            { 1, "🌑" }, {2, "🌘" }, {3, "🌗" }, {4, "🌖" }, {5, "🌕" }
        };

        /// <summary>
        /// Calculate votes sum for active votes
        /// </summary>
        /// <returns></returns>
        public async Task<List<ActiveContestEntry>> ConsolidateActiveVotes()
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

        private string GetVotingStartedMessage(SystemState state)
        {
            var deadlineText = _timeService.FormatDateAndTimeToAnnouncementTimezone(state.NextDeadlineUTC);

            return LocTokens.SubstituteTokens(_loc.VotingStarted,
                Tuple.Create(LocTokens.VotingChannelLink, _botConfiguration.VotingChannelInviteLink),
                Tuple.Create(LocTokens.Deadline, deadlineText));
        }

        private async void OnWinnerChosen(User winner, ActiveContestEntry winningEntry)
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
    }
}