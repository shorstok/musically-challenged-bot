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
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public enum VotingFinalizationResult
    {
        Ok,
        NotEnoughVotes,
        NotEnoughContesters,
        Halt
    }

    public abstract class VotingControllerBase<TVotable, TVote> : ITelegramQueryHandler
        where TVotable : IVotable where TVote : IVote
    {
        protected readonly ITelegramClient _client;
        protected readonly IBotConfiguration _botConfiguration;
        protected readonly IRepository _repository;
        protected readonly LocStrings _loc;
        protected readonly CrypticNameResolver _crypticNameResolver;
        protected readonly BroadcastController _broadcastController;
        protected readonly TimeService _timeService;

        private static readonly ILog logger = Log.Get(typeof(VotingControllerBase<TVotable, TVote>));
        readonly Random _random = new Random();
        private Throttle _votingStatsUpdateThrottle = new Throttle(TimeSpan.FromSeconds(20));

        public VotingControllerBase(
            ITelegramClient client,
            IBotConfiguration botConfiguration,
            IRepository repository,
            LocStrings loc,
            CrypticNameResolver crypticNameResolver,
            BroadcastController broadcastController,
            TimeService timeService)
        {
            _client = client;
            _botConfiguration = botConfiguration;
            _repository = repository;
            _loc = loc;
            _crypticNameResolver = crypticNameResolver;
            _broadcastController = broadcastController;
            _timeService = timeService;
        }

        public abstract string Prefix { get; }

        public abstract Dictionary<int, string> VotingSmiles { get; }
        protected abstract string _weHaveAWinnerTemplate { get; }
        protected abstract string _weHaveWinnersTemplate { get; }
        protected abstract string _votingStartedTemplate { get; }

        // things controllers need to set up. The default values are essentially doNothing functions
        // db interactions
        protected abstract bool SetOrUpdateVote(User user, int voteVal, int entryId);
        protected abstract IEnumerable<TVotable> GetActiveEntries();
        protected abstract IEnumerable<Tuple<TVote, User>> GetVotesForEntry(int entryId);
        protected abstract TVotable GetExistingEntry(int entryId);
        protected abstract Instant ScheduleNextDeadline();
        protected abstract Task<List<TVotable>> ConsolidateActiveVotes();

        // message templates
        protected abstract string GetVoteDescriptionRealVotes(TVote vote);
        protected abstract string GetEntryText(User user, string votingDetails, string extra);


        // finalizing voting
        protected virtual async Task OnEnteredFinalization() =>
            Task.Run(() => { });
        protected virtual List<TVotable> _filterConsolidatedEntriesIfEnoughContester(List<TVotable> entries) =>
            entries;
        protected abstract Task OnWinnerChosen(User winner, TVotable winningEntry);

        public async Task ExecuteQuery(CallbackQuery callbackQuery)
        {
            var data = CommandManager.ExtractQueryData(this, callbackQuery);

            var user = _repository.CreateOrGetUserByTgIdentity(callbackQuery.From);

            if (user.State == UserState.Banned)
            {
                await _client.AnswerCallbackQueryAsync(callbackQuery.Id, _loc.YouAreBanned, true);
                return;
            }

            if (!TryParseQueryData(data, out var voteVal, out var entryId))
            {
                logger.Error($"Invalid voting data: {data}, parsing failed");

                await _client.AnswerCallbackQueryAsync(callbackQuery.Id, _loc.NotAvailable, true);
                return;
            }

            //If no votes were cast in this tour, create default values for all entries except entryId

            var updated = SetOrUpdateVote(user, voteVal, entryId);

            await _client.AnswerCallbackQueryAsync(callbackQuery.Id,
                LocTokens.SubstituteTokens(updated ? _loc.VoteUpdated : _loc.ThankYouForVote,
                    Tuple.Create(LocTokens.VoteCount, voteVal.ToString()),
                    Tuple.Create(LocTokens.User, _crypticNameResolver.GetCrypticNameFor(user))
                    ), updated);

            _votingStatsUpdateThrottle.WaitAsync(() => UpdateAllVotesThrottled(false), CancellationToken.None).ConfigureAwait(false);
        }

        private async Task UpdateAllVotesThrottled(bool showRealVotes = false)
        {
            await UpdateVotingStats(showRealVotes);
            await MaybePingAllEntries(showRealVotes);
        }

        private async Task UpdateVotingStats(bool showRealVotes)
        {
            var state = _repository.GetOrCreateCurrentState();

            if (null == state.CurrentVotingStatsMessageId || null == state.VotingChannelId)
                return;

            var activeEntries = GetActiveEntries().ToArray();

            var builder = new StringBuilder();

            builder.Append(_loc.VotingStatsHeader);

            var usersAndVoteCount = new Dictionary<User, int>();

            foreach (var entry in activeEntries)
            {
                var votes = GetVotesForEntry(entry.Id).ToArray();
                var entryAuthor = _repository.GetExistingUserWithTgId(entry.AuthorUserId);

                if (votes.Length == 0)
                    usersAndVoteCount[entryAuthor] = 0;
                else
                    usersAndVoteCount[entryAuthor] = votes.Sum(v => v.Item1.Value);
            }

            var votesOrdered = usersAndVoteCount.
                GroupBy(g => g.Value, pair => pair.Key).
                OrderByDescending(g => g.Key).
                ToArray();

            var medals = new[] { "🥇", "🥈", "🥉" };

            builder.AppendLine("");

            if (showRealVotes)
            {
                for (int place = 0; place < votesOrdered.Length; place++)
                {
                    var users = votesOrdered[place].OrderBy(u => u.Username ?? u.Name).ToArray();

                    for (int subitem = 0; subitem < users.Length; subitem++)
                    {
                        var user = users[subitem];
                        bool isLast = place == votesOrdered.Length - 1 && subitem == users.Length - 1;

                        builder.AppendLine("<code>│</code>");

                        builder.Append(isLast ? "<code>┕ </code>" : "<code>┝ </code>");

                        if (medals.Length > place)
                            builder.Append(medals[place]);

                        builder.Append(user.Username ?? user.Name);

                        builder.AppendLine($"<code> - {votesOrdered[place].Key}</code>");
                    }

                }
            }
            else
            {
                builder.AppendLine("Votes hidden 😏");
            }

            await _client.EditMessageTextAsync(state.VotingChannelId.Value, state.CurrentVotingStatsMessageId.Value,
                builder.ToString(), ParseMode.Html);
        }

        private async Task MaybePingAllEntries(bool showRealVotes)
        {
            var activeEntries = GetActiveEntries().ToArray();

            //Slowly walk over all contest entries
            foreach (var activeContestEntry in activeEntries)
            {
                await UpdateVotingIndicatorForEntry(activeContestEntry.Id, showRealVotes);
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        private async Task UpdateVotingIndicatorForEntry(int entryId, bool showRealVotes)
        {
            var votes = GetVotesForEntry(entryId).ToArray();
            var entry = GetExistingEntry(entryId);

            if (null == entry)
            {
                logger.Error($"Could not find entry with id {entryId}");
                return;
            }

            var author = _repository.GetExistingUserWithTgId(entry.AuthorUserId);

            if (null == author)
            {
                logger.Error($"Could not find author with id {entry.AuthorUserId}");
                return;
            }

            StringBuilder builder = new StringBuilder();

            if (votes.Any())
            {
                builder.AppendLine();

                foreach (var tuple in votes)
                {
                    string voteDescr = GetVoteDescriptionRealVotes(tuple.Item1);

                    builder.AppendLine(showRealVotes
                        ? $"<code>{_crypticNameResolver.GetCrypticNameFor(tuple.Item2)}</code>: <b>{voteDescr}</b>"
                        : $"{tuple.Item2?.GetHtmlUserLink() ?? "??"}: <b>😏</b>");
                }
            }

            await _client.EditMessageTextAsync(entry.ContainerChatId, entry.ContainerMesssageId,
                GetEntryText(author, builder.ToString(), entry.Description),
                ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(CreateVotingButtonsForEntry(entry)));
        }

        /// <summary>
        /// Create voting controls in voting channel, announce about voting start
        /// </summary>
        /// <returns></returns>
        public async Task StartVotingAsync()
        {
            var activeEntries = GetActiveEntries();

            _crypticNameResolver.Reset();

            var deadline = ScheduleNextDeadline();

            foreach (var activeEntry in activeEntries)
                await CreateVotingControlsForEntry(activeEntry);

            //Get new deadline
            var state = _repository.GetOrCreateCurrentState();

            var votingMesasge = await _broadcastController.AnnounceInMainChannel(GetVotingStartedMessage(state), true);

            if (null != votingMesasge)
                _repository.UpdateState(x => x.CurrentVotingDeadlineMessageId, votingMesasge.MessageId);

            await CreateVotingStatsMessageAsync();
        }

        private async Task CreateVotingStatsMessageAsync()
        {
            var votingStatsMessage = await _broadcastController.AnnounceInVotingChannel(_loc.VotingStatsHeader, false);
            _repository.UpdateState(x => x.CurrentVotingStatsMessageId, votingStatsMessage?.MessageId);
        }

        public async Task<Tuple<VotingFinalizationResult, User>> FinalizeVoting()
        {
            try
            {
                await UpdateVotingStats(true);

                var entries = await ConsolidateActiveVotes();

                await OnEnteredFinalization();

                if (!entries.Any())
                {
                    logger.Warn($"ConsolidateActiveVotes found no active entries, announcing and switching to standby");

                    await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughEntriesAnnouncement,
                        pin: true);

                    return Tuple.Create<VotingFinalizationResult, User>(VotingFinalizationResult.NotEnoughContesters, null);
                }

                if (entries.Count > 2)
                {
                    logger.Info("filtering consolidated entries");
                    _filterConsolidatedEntriesIfEnoughContester(entries);
                }

                var winnersGroup = entries.GroupBy(e => e.ConsolidatedVoteCount ?? 0).OrderByDescending(g => g.Key)
                    .FirstOrDefault();

                //wtf, not expected
                if (winnersGroup == null)
                {
                    logger.Error("WinnersGroup is null, not expected");
                    return Tuple.Create<VotingFinalizationResult, User>(VotingFinalizationResult.Halt, null);
                }

                var voteCount = winnersGroup?.Key ?? 0;

                if (voteCount < _botConfiguration.MinAllowedVoteCountForWinners)
                {
                    logger.Warn(
                        $"Winners got {winnersGroup?.Key} votes total, that's less than _configuration.MinAllowedVoteCountForWinners ({_botConfiguration.MinAllowedVoteCountForWinners}), " +
                        $"announcing and switching to standby");

                    await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughVotesAnnouncement, true,
                        Tuple.Create(LocTokens.VoteCount, voteCount.ToString()));

                    return Tuple.Create<VotingFinalizationResult, User>(VotingFinalizationResult.NotEnoughVotes, null);
                }

                //OK, we got valid winner(s)

                var winnerDictionary = new Dictionary<TVotable, User>();

                foreach (var entry in winnersGroup)
                {
                    var user = _repository.GetExistingUserWithTgId(entry.AuthorUserId);

                    if (user == null)
                    {
                        logger.Error($"user with ID == {entry.AuthorUserId} not found, skipping");
                        continue;
                    }

                    winnerDictionary[entry] = user;
                }

                if (!winnerDictionary.Any())
                {
                    //Most voted user was deleted?

                    logger.Error("entryAndUsers.Count=0 unexpected");

                    return Tuple.Create<VotingFinalizationResult, User>(VotingFinalizationResult.Halt, null);
                }

                User actualWinner = null;
                TVotable winningEntry = default(TVotable);

                if (winnerDictionary.Count == 1)
                {
                    //we have 1 winner, that's easy: just announce him

                    var pair = winnerDictionary.FirstOrDefault();

                    actualWinner = pair.Value;
                    winningEntry = pair.Key;

                    await _broadcastController.AnnounceInMainChannel(_weHaveAWinnerTemplate, false,
                        Tuple.Create(LocTokens.User, actualWinner.GetHtmlUserLink()),
                        Tuple.Create(LocTokens.VoteCount, voteCount.ToString()));
                }
                else
                {
                    //we have more than 1 winner, announce everyone, select `actual` winner using random

                    var pair = winnerDictionary.ElementAt(_random.Next(winnerDictionary.Count - 1));

                    actualWinner = pair.Value;
                    winningEntry = pair.Key;

                    var winnersList = string.Join(", ",
                        winnerDictionary.Values.Select(u => u.GetHtmlUserLink()));

                    await _broadcastController.AnnounceInMainChannel(_weHaveWinnersTemplate, false,
                        Tuple.Create(LocTokens.User, actualWinner.GetHtmlUserLink()),
                        Tuple.Create(LocTokens.Users, winnersList),
                        Tuple.Create(LocTokens.VoteCount, voteCount.ToString()));
                }

                //forward winner's entry to main channel
                var state = _repository.GetOrCreateCurrentState();

                await OnWinnerChosen(actualWinner, winningEntry);

                return Tuple.Create(VotingFinalizationResult.Ok, actualWinner);
            }
            catch (Exception e)
            {
                logger.Error($"Unexpected exception: {e}");
                throw e;
            }
        }

        private async Task CreateVotingControlsForEntry(TVotable activeEntry)
        {
            var inlineKeyboardButtons = CreateVotingButtonsForEntry(activeEntry);

            var message = await _client.EditMessageReplyMarkupAsync(activeEntry.ContainerChatId,
                activeEntry.ContainerMesssageId, new InlineKeyboardMarkup(inlineKeyboardButtons));

            if (null == message)
            {
                logger.Error($"Couldnt create voting controls for contest entry {activeEntry.Id}");
                return;
            }
        }

        private List<InlineKeyboardButton> CreateVotingButtonsForEntry(TVotable activeEntry)
        {
            var inlineKeyboardButtons = new List<InlineKeyboardButton>();

            foreach (var pair in VotingSmiles.OrderBy(x => x.Key))
            {
                inlineKeyboardButtons.Add(InlineKeyboardButton.WithCallbackData(
                    pair.Value,
                    CreateQueryDataForEntryAndValue(pair.Key, activeEntry)));
            }

            return inlineKeyboardButtons;
        }

        private const char QuerySeparatorChar = '/';

        private bool TryParseQueryData(string data, out int value, out int entryId)
        {
            value = 0;
            entryId = 0;

            if (string.IsNullOrWhiteSpace(data))
                return false;

            var blocks = data.Split(QuerySeparatorChar);

            if (blocks.Length != 2)
                return false;

            if (!int.TryParse(blocks[0], out value))
                return false;

            if (!int.TryParse(blocks[1], out entryId))
                return false;

            return true;
        }

        private string CreateQueryDataForEntryAndValue(int i, TVotable activeEntry) =>
            CommandManager.ConstructQueryData(this, $"{i}{QuerySeparatorChar}{activeEntry.Id}");

        protected string GetVotingStartedMessage(SystemState state)
        {
            var deadlineText = _timeService.FormatDateAndTimeToAnnouncementTimezone(state.NextDeadlineUTC);

            return LocTokens.SubstituteTokens(_votingStartedTemplate,
                Tuple.Create(LocTokens.VotingChannelLink, _botConfiguration.VotingChannelInviteLink),
                Tuple.Create(LocTokens.Deadline, deadlineText));
        }
    }
}
