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

    public class VotingControllerHelper<TVotable, TVote> where TVotable : IVotable where TVote : IVote
    {
        private readonly ITelegramClient _client;
        private readonly IBotConfiguration _botConfiguration;
        private readonly IRepository _repository;
        private readonly LocStrings _loc;
        private readonly CrypticNameResolver _crypticNameResolver;
        private readonly BroadcastController _broadcastController;

        private static readonly ILog logger = Log.Get(typeof(VotingControllerHelper<TVotable, TVote>));
        readonly Random _random = new Random();
        private Throttle _votingStatsUpdateThrottle = new Throttle(TimeSpan.FromSeconds(20));

        public VotingControllerHelper(
            ITelegramClient client,
            IBotConfiguration botConfiguration,
            IRepository repository,
            LocStrings loc,
            CrypticNameResolver crypticNameResolver,
            BroadcastController broadcastController)
        {
            _client = client;
            _botConfiguration = botConfiguration;
            _repository = repository;
            _loc = loc;
            _crypticNameResolver = crypticNameResolver;
            _broadcastController = broadcastController;
        }

        // things controllers need to set up. The default values are essentially doNothing functions
        // controller reference for queries
        private ITelegramQueryHandler _controller;

        public void SetController(ITelegramQueryHandler controller) =>
            _controller = controller;

        // db interactions
        private Func<User, int, int, bool?> _setOrUpdateVote = (user, voteVal, entryId) => null;
        private Func<IEnumerable<TVotable>> _getActiveEntries = () => Enumerable.Empty<TVotable>();
        private Func<int, IEnumerable<Tuple<TVote, User>>> _getVotesForEntry = _ => Enumerable.Empty<Tuple<TVote, User>>();
        private Func<int, TVotable> _getExistingEntry = _ => default(TVotable);
        private Func<Instant> _scheduleNextDeadline = () => default(Instant);
        private Func<Task<List<TVotable>>> _consolidateActiveVotes = () => new Task<List<TVotable>>(() => new List<TVotable>());

        public void ConfigureDbInteraction(
            Func<User, int, int, bool?> setOrUpdateVote,
            Func<IEnumerable<TVotable>> getActiveEntries,
            Func<int, IEnumerable<Tuple<TVote, User>>> getVotesForEntry,
            Func<int, TVotable> getExistingEntry,
            Func<Instant> scheduleNextDeadline,
            Func<Task<List<TVotable>>> consolidateActiveVotes)
        {
            _setOrUpdateVote = setOrUpdateVote;
            _getActiveEntries = getActiveEntries;
            _getVotesForEntry = getVotesForEntry;
            _getExistingEntry = getExistingEntry;
            _scheduleNextDeadline = scheduleNextDeadline;
            _consolidateActiveVotes = consolidateActiveVotes;
        }

        // message templates
        private Dictionary<int, string> _votingSmiles;
        private Func<TVote, string> _getVoteDescriptionRealVotes = _ => string.Empty;
        private Func<User, string, string, string> _getEntryText = (x, y, z) => string.Empty;
        private Func<SystemState, string> _getVotingStartedMessage = _ => string.Empty;
        private string _weHaveAWinnerTemplate = string.Empty;
        private string _weHaveWinnersTemplate = string.Empty;

        public void ConfigureMessageTemplates(
            Dictionary<int, string> votingSmiles,
            Func<TVote, string> getVoteDescriptionRealVotes,
            Func<User, string, string, string> getEntryText,
            Func<SystemState, string> getVotingStartedMessage,
            string weHaveAWinnerTemplate,
            string weHaveWinnersTemplate)
        {
            _votingSmiles = votingSmiles;
            _getVoteDescriptionRealVotes = getVoteDescriptionRealVotes;
            _getEntryText = getEntryText;
            _getVotingStartedMessage = getVotingStartedMessage;
            _weHaveAWinnerTemplate = weHaveAWinnerTemplate;
            _weHaveWinnersTemplate = weHaveWinnersTemplate;
        }

        // finalizing voting
        private Action _onEnteredFinalization = () => { };
        private Func<List<TVotable>, List<TVotable>> _filterConsolidatedEntriesIfEnoughContester = x => x;
        private Action<User, TVotable> _onWinnerChosen = (x, y) => new Task(() => { });

        public void ConfigureVotingFinalization(
            Action<User, TVotable> onWinnerChosen,
            Action onEnteredFinalization = null,
            Func<List<TVotable>, List<TVotable>> filterConsolidatedEntriesIfEnoughContester = null)
        {
            _onWinnerChosen = onWinnerChosen;
            _onEnteredFinalization = onEnteredFinalization ?? _onEnteredFinalization;
            _filterConsolidatedEntriesIfEnoughContester = 
                filterConsolidatedEntriesIfEnoughContester ?? _filterConsolidatedEntriesIfEnoughContester;
        }



        public async Task ExecuteQuery(CallbackQuery callbackQuery)
        {
            var data = CommandManager.ExtractQueryData(_controller, callbackQuery);

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

            var updated = _setOrUpdateVote(user, voteVal, entryId);

            if (updated == null)
                return;

            await _client.AnswerCallbackQueryAsync(callbackQuery.Id,
                LocTokens.SubstituteTokens(updated.Value ? _loc.VoteUpdated : _loc.ThankYouForVote,
                    Tuple.Create(LocTokens.VoteCount, voteVal.ToString()),
                    Tuple.Create(LocTokens.User, _crypticNameResolver.GetCrypticNameFor(user))
                    ), updated.Value);

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

            var activeEntries = _getActiveEntries().ToArray();

            var builder = new StringBuilder();

            builder.Append(_loc.VotingStatsHeader);

            var usersAndVoteCount = new Dictionary<User, int>();

            foreach (var entry in activeEntries)
            {
                var votes = _getVotesForEntry(entry.Id).ToArray();
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
            var activeEntries = _getActiveEntries().ToArray();

            //Slowly walk over all contest entries
            foreach (var activeContestEntry in activeEntries)
            {
                await UpdateVotingIndicatorForEntry(activeContestEntry.Id, showRealVotes);
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        private async Task UpdateVotingIndicatorForEntry(int entryId, bool showRealVotes)
        {
            var votes = _getVotesForEntry(entryId).ToArray();
            var entry = _getExistingEntry(entryId);

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
                    string voteDescr = _getVoteDescriptionRealVotes(tuple.Item1);

                    builder.AppendLine(showRealVotes
                        ? $"<code>{_crypticNameResolver.GetCrypticNameFor(tuple.Item2)}</code>: <b>{voteDescr}</b>"
                        : $"{tuple.Item2?.GetHtmlUserLink() ?? "??"}: <b>😏</b>");
                }
            }

            await _client.EditMessageTextAsync(entry.ContainerChatId, entry.ContainerMesssageId,
                _getEntryText(author, builder.ToString(), entry.Description),
                ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(CreateVotingButtonsForEntry(entry)));
        }

        public async Task StartVotingAsync()
        {
            var activeEntries = _getActiveEntries();

            _crypticNameResolver.Reset();

            var deadline = _scheduleNextDeadline();

            foreach (var activeEntry in activeEntries)
                await CreateVotingControlsForEntry(activeEntry);

            //Get new deadline
            var state = _repository.GetOrCreateCurrentState();

            var votingMesasge = await _broadcastController.AnnounceInMainChannel(_getVotingStartedMessage(state), true);

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
            await UpdateVotingStats(true);

            var entries = await _consolidateActiveVotes();

            _onEnteredFinalization();

            if (!entries.Any())
            {
                logger.Warn($"ConsolidateActiveVotes found no active entries, announcing and switching to standby");

                await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughEntriesAnnouncement,
                    pin: true);

                return Tuple.Create<VotingFinalizationResult, User>(VotingFinalizationResult.NotEnoughContesters, null);
            }

            if (entries.Count > 2)
            {
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

            _onWinnerChosen(actualWinner, winningEntry);

            return Tuple.Create(VotingFinalizationResult.Ok, actualWinner);
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

            foreach (var pair in _votingSmiles.OrderBy(x => x.Key))
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
            CommandManager.ConstructQueryData(_controller, $"{i}{QuerySeparatorChar}{activeEntry.Id}");
    }
}
