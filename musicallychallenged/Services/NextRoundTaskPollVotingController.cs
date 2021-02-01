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
        private readonly CrypticNameResolver _crypticNameResolver;
        private readonly BroadcastController _broadcastController;
        private readonly ITelegramClient _client;

        private readonly Random _random = new Random();

        private Throttle _votingStatsUpdateThrottle = new Throttle(TimeSpan.FromSeconds(20));

        private static readonly ILog logger = Log.Get(typeof(NextRoundTaskPollVotingController));

        public string Prefix { get; } = "nv";

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

            if (_repository.MaybeCreateSuggestionVoteForAllActiveEntriesExcept(user, entryId, 0))
                logger.Info($"Set default suggestion vote value of 0 for user {user.GetUsernameOrNameWithCircumflex()} for all active entries except {entryId}");

            _repository.SetOrUpdateTaskPollVote(user, entryId, voteVal, out var updated);

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} {(updated ? "updated vote" : "voted")} {voteVal} for entry {entryId}");

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

        public async Task UpdateVotingStats(bool showRealVotes)
        {
            var state = _repository.GetOrCreateCurrentState();

            if (null == state.CurrentVotingStatsMessageId || null == state.VotingChannelId)
                return;

            var activeSuggestions = _repository.GetActiveTaskSuggestions().ToArray();

            var builder = new StringBuilder();

            builder.Append(_loc.VotingStatsHeader);

            var usersAndVoteCount = new Dictionary<User, int>();

            foreach (var suggestion in activeSuggestions)
            {
                var votes = _repository.GetVotesForTaskSuggestion(suggestion.Id).ToArray();
                var entryAuthor = _repository.GetExistingUserWithTgId(suggestion.AuthorUserId);

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

        public async Task MaybePingAllEntries(bool showRealVotes)
        {
            var activeSuggestions = _repository.GetActiveTaskSuggestions().ToArray();

            //Slowly walk over all contest entries
            foreach (var suggestion in activeSuggestions)
            {
                await UpdateVotingIndicatorForEntry(suggestion.Id, showRealVotes);
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        public async Task UpdateVotingIndicatorForEntry(int entryId, bool showRealVotes)
        {
            var votes = _repository.GetVotesForTaskSuggestion(entryId).ToArray();
            var entry = _repository.GetExistingTaskSuggestion(entryId);

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
                    var heartBuider = new StringBuilder();

                    string voteDescr = "*";

                    var smileIndex = tuple.Item1.Value - _botConfiguration.MinSuggestionVoteValue;

                    if (smileIndex < _votingSmiles.Length && smileIndex >= 0)
                        voteDescr = _votingSmiles[smileIndex];

                    for (int i = 1; i <= smileIndex + 1; i++)
                    {
                        heartBuider.Append(voteDescr);
                    }

                    builder.AppendLine(showRealVotes
                        ? $"<code>{_crypticNameResolver.GetCrypticNameFor(tuple.Item2)}</code>: <b>{heartBuider.ToString()}</b>"
                        : $"{tuple.Item2?.GetHtmlUserLink() ?? "??"}: <b>😏</b>");
                }
            }

            await _client.EditMessageTextAsync(entry.ContainerChatId, entry.ContainerMesssageId,
                _pollController.GetTaskSuggestionMessageText(author, builder.ToString(), entry.Description),
                ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(CreateVotingButtonsForEntry(entry)));
        }

        public async Task StartVotingAsync()
        {
            var activeSuggestions = _repository.GetActiveTaskSuggestions();
            _crypticNameResolver.Reset();

            var deadline = _timeService.ScheduleNextDeadlineIn(_botConfiguration.TaskSuggestionVotingDeadlineTimeHours);

            foreach (var suggestion in activeSuggestions)
                await CreateVotingControlsForEntry(suggestion);

            //Get new deadline
            var state = _repository.GetOrCreateCurrentState();

            var votingMesasge = await _broadcastController.AnnounceInMainChannel(GetVotingStartedMessage(state), true);

            if (null != votingMesasge)
                _repository.UpdateState(x => x.CurrentVotingDeadlineMessageId, votingMesasge.MessageId);

            await CreateVotingStatsMessageAsync();
        }

        private string GetVotingStartedMessage(SystemState state)
        {
            var deadlineText = _timeService.FormatDateAndTimeToAnnouncementTimezone(state.NextDeadlineUTC);

            return LocTokens.SubstituteTokens(_loc.TaskSuggestionVotingStarted,
                Tuple.Create(LocTokens.VotingChannelLink, _botConfiguration.VotingChannelInviteLink),
                Tuple.Create(LocTokens.Deadline, deadlineText));
        }

        public async Task CreateVotingStatsMessageAsync()
        {
            var votingStatsMessage = await _broadcastController.AnnounceInVotingChannel(_loc.VotingStatsHeader, false);
            _repository.UpdateState(x => x.CurrentVotingStatsMessageId, votingStatsMessage?.MessageId);
        }

        public async Task<Tuple<VotingController.FinalizationResult, User>> FinalizeVoting()
        {
            await UpdateVotingStats(true);

            var entries = await ConsolidateActiveVotes();
            var state = _repository.GetOrCreateCurrentState();

            if (!entries.Any())
            {
                logger.Warn($"ConsolidateActiveVotes found no active entries, announcing and switching to standby");

                await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughEntriesAnnouncement,
                    pin: true);

                return Tuple.Create<VotingController.FinalizationResult, User>(VotingController.FinalizationResult.NotEnoughContesters,null);
            }

            if (entries.Count > 2)
            {
                //Exclude last winner from new voting, if there are enough competitors
                entries.RemoveAll(e => e.AuthorUserId == state.CurrentWinnerId);
            }

            var winnersGroup = entries.GroupBy(e => e.ConsolidatedVoteCount ?? 0).OrderByDescending(g => g.Key)
                .FirstOrDefault();

            //wtf, not expected
            if (winnersGroup == null)
            {
                logger.Error("WinnersGroup is null, not expected");
                return Tuple.Create<VotingController.FinalizationResult, User>(VotingController.FinalizationResult.Halt,null);
            }

            var voteCount = winnersGroup?.Key ?? 0;

            if (voteCount < _botConfiguration.MinAllowedVoteCountForWinners)
            {
                logger.Warn(
                    $"Winners got {winnersGroup?.Key} votes total, that's less than _configuration.MinAllowedVoteCountForWinners ({_botConfiguration.MinAllowedVoteCountForWinners}), " +
                    $"announcing and switching to standby");

                await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughVotesAnnouncement, true,
                    Tuple.Create(LocTokens.VoteCount, voteCount.ToString()));
                
                return Tuple.Create<VotingController.FinalizationResult, User>(VotingController.FinalizationResult.NotEnoughVotes,null);
            }

            //OK, we got valid winner(s)

            var winnerDictionary = new Dictionary<TaskSuggestion, User>();

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

                return Tuple.Create<VotingController.FinalizationResult, User>(VotingController.FinalizationResult.Halt,null);                
            }

            User actualWinner = null;
            TaskSuggestion winningEntry = null;

            if (winnerDictionary.Count == 1)
            {
                //we have 1 winner, that's easy: just announce him

                var pair = winnerDictionary.FirstOrDefault();

                actualWinner = pair.Value;
                winningEntry = pair.Key;

                await _broadcastController.AnnounceInMainChannel(_loc.WeHaveAWinnerTaskSuggestion, false,
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

                await _broadcastController.AnnounceInMainChannel(_loc.WeHaveWinnersTaskSuggestion, false,
                    Tuple.Create(LocTokens.User, actualWinner.GetHtmlUserLink()),
                    Tuple.Create(LocTokens.Users, winnersList),
                    Tuple.Create(LocTokens.VoteCount, voteCount.ToString()));
            }

            //update current task template
            _repository.UpdateState(s => s.CurrentTaskTemplate, winningEntry.Description);

            return Tuple.Create(VotingController.FinalizationResult.Ok, actualWinner);
        }

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

        private async Task CreateVotingControlsForEntry(TaskSuggestion suggestion)
        {
            var inlineKeyboardButtons = CreateVotingButtonsForEntry(suggestion);

            var message = await _client.EditMessageReplyMarkupAsync(suggestion.ContainerChatId,
                suggestion.ContainerMesssageId, new InlineKeyboardMarkup(inlineKeyboardButtons));

            if (null == message)
            {
                logger.Error($"Couldnt create voting controls for contest entry {suggestion.Id}");
                return;
            }
        }

        public static readonly string[] _votingSmiles = new[] { "👎", "🤷‍", "👍"};

        private List<InlineKeyboardButton> CreateVotingButtonsForEntry(TaskSuggestion suggestion)
        {
            var inlineKeyboardButtons = new List<InlineKeyboardButton>();

            for (int i = _botConfiguration.MinSuggestionVoteValue; i <= _botConfiguration.MaxSuggestionVoteValue; i++)
            {
                var smileIndex = i - _botConfiguration.MinSuggestionVoteValue;

                inlineKeyboardButtons.Add(InlineKeyboardButton.WithCallbackData(
                    smileIndex < _votingSmiles.Length ? _votingSmiles[smileIndex] : i.ToString(),
                    CreateQueryDataForEntryAndValue(i, suggestion)));
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
            
        private string CreateQueryDataForEntryAndValue(int i, TaskSuggestion suggestion) =>
            CommandManager.ConstructQueryData(this, $"{i}{QuerySeparatorChar}{suggestion.Id}");
    }
}
