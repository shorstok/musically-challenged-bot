using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
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
        private readonly BotConfiguration _botConfiguration;
        private readonly TimeService _timeService;
        private readonly LocStrings _loc;
        private readonly ContestController _contestController;
        private readonly BroadcastController _broadcastController;
        private readonly ITelegramClient _client;

        public string Prefix { get; } = "v";

        public VotingController(IRepository repository,
            BotConfiguration botConfiguration,
            TimeService timeService,
            LocStrings loc,
            ContestController contestController,
            BroadcastController broadcastController,
            ITelegramClient client)
        {
            _repository = repository;
            _botConfiguration = botConfiguration;
            _timeService = timeService;
            _loc = loc;
            _contestController = contestController;
            _broadcastController = broadcastController;
            _client = client;         
        }

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

            _repository.SetOrRetractVote(user,entryId,voteVal, out var retracted);

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} voted {voteVal} for entry {entryId}");

            await _client.AnswerCallbackQueryAsync(callbackQuery.Id, 
                LocTokens.SubstituteTokens(retracted ? _loc.VoteRemoved: _loc.ThankYouForVote,
                    Tuple.Create(LocTokens.VoteCount,voteVal.ToString())), 
                true);

            await UpdateVotingIndicatorForEntry(entryId);
            await UpdateVotingStats();
        }

        public async Task UpdateVotingStats()
        {
            var state = _repository.GetOrCreateCurrentState();

            if(null == state.CurrentVotingStatsMessageId || null == state.VotingChannelId)
                return;

            var activeEntries = _repository.GetActiveContestEntries().ToArray();

            var builder = new StringBuilder();

            builder.Append(_loc.VotigStatsHeader);

            var usersAndVoteCount = new Dictionary<User, int>();

            foreach (var activeContestEntry in activeEntries)
            {
                var votes = _repository.GetVotesForEntry(activeContestEntry.Id).ToArray();
                var user = _repository.GetExistingUserWithTgId(activeContestEntry.AuthorUserId);

                if (votes.Length == 0)
                    usersAndVoteCount[user] = 0;
                else
                    usersAndVoteCount[user] = votes.Sum(v => v.Item1.Value);
            }

            var votesOrdered = usersAndVoteCount.
                GroupBy(g=>g.Value,pair => pair.Key).
                OrderByDescending(g=>g.Key).
                ToArray();

            var medals = new[] {"🥇","🥈","🥉"};

            builder.AppendLine("");
            
            for (int place = 0; place < votesOrdered.Length; place++)
            {
                var users = votesOrdered[place].OrderBy(u=>u.Username ?? u.Name).ToArray();

                for (int subitem = 0; subitem < users.Length; subitem++)
                {
                    var user = users[subitem];
                    bool isLast = place == votesOrdered.Length - 1 && subitem == users.Length - 1;

                    builder.AppendLine("<code>│</code>");

                    builder.Append(isLast?"<code>┕ </code>":"<code>┝ </code>");

                    if (medals.Length > place)
                        builder.Append(medals[place]);

                    builder.Append(user.Username ?? user.Name);

                    builder.AppendLine($"<code> - {votesOrdered[place].Key}</code>");
                }
                
            }
            
            await _client.EditMessageTextAsync(state.VotingChannelId.Value, state.CurrentVotingStatsMessageId.Value,
                builder.ToString(), ParseMode.Html);
        }

        private readonly string[] _votingSmiles = new[] {"🤭", "🥴","😐","🙂","🤩"};
        

        public async Task UpdateVotingIndicatorForEntry(int entryId)
        {
            var votes = _repository.GetVotesForEntry(entryId).ToArray();
            var entry = _repository.GetExistingEntry(entryId);
            
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
                builder.AppendLine();

                foreach (var tuple in votes)
                {
                    var heartBuider = new StringBuilder();

                    string voteDescr = "*";

                    var smileIndex = tuple.Item1.Value - _botConfiguration.MinVoteValue;

                    if (smileIndex < _votingSmiles.Length && smileIndex>=0)
                        voteDescr = _votingSmiles[smileIndex];

                    for (int i = 1; i <= smileIndex+1; i++)
                    {
                        heartBuider.Append(voteDescr);
                    }

                    builder.AppendLine($"<code>{tuple.Item2.Username ?? tuple.Item2.Name}</code>: <b>{heartBuider.ToString()}</b>");
                }
            }
            
            await _client.EditMessageTextAsync(entry.ContainerChatId, entry.ContainerMesssageId,
                _contestController.GetContestEntryText(author, builder.ToString(), entry.Description),ParseMode.Html,
                replyMarkup:new InlineKeyboardMarkup(CreateVotingButtonsForEntry(entry)));
        }

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

                if(null == user)
                    continue;
                
                votingResults.AppendLine($"{user.GetHtmlUserLink()} : {entry.ConsolidatedVoteCount ?? 0}");
            }

            await _broadcastController.AnnounceInMainChannel(_loc.VotigResultsTemplate, false,
                Tuple.Create(LocTokens.Users,votingResults.ToString()));

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

        /// <summary>
        /// Create voting controls in voting channel, announce about voting start
        /// </summary>
        /// <returns></returns>
        public async Task InitiateVotingAsync()
        {
            var activeEntries = _repository.GetActiveContestEntries();
            var state = _repository.GetOrCreateCurrentState();
            
            var deadline = _timeService.ScheduleNextDeadlineIn(state.VotingDurationDays ?? 2, 22);

            foreach (var activeEntry in activeEntries)
                await CreateVotingControlsForEntry(activeEntry);

            var deadlineText = _timeService.FormatDateAndTimeToAnnouncementTimezone(deadline);

            await _broadcastController.AnnounceInMainChannel(_loc.VotingStarted, true,
                Tuple.Create(LocTokens.VotingChannelLink,_botConfiguration.VotingChannelInviteLink),
                Tuple.Create(LocTokens.Deadline,deadlineText));
            
            await CreateVotingStatsMessageAsync();
        }

        public async Task CreateVotingStatsMessageAsync()
        {
            var votingStatsMessage = await _broadcastController.AnnounceInVotingChannel(_loc.VotigStatsHeader, false);        
            _repository.UpdateState(x => x.CurrentVotingStatsMessageId, votingStatsMessage?.MessageId);
        }

        readonly Random _random = new Random();

        public enum FinalizationResult
        {
            Ok,
            NotEnoughVotes,
            NotEnoughContesters,
            Halt
        }

        public async Task<Tuple<FinalizationResult, User>> FinalizeVoting()
        {
            var entries = await ConsolidateActiveVotes();
            var state = _repository.GetOrCreateCurrentState();

            _repository.UpdateState(x=>x.CurrentChallengeRoundNumber, state.CurrentChallengeRoundNumber+1);          
            logger.Info($"Challenge round number set to {state.CurrentChallengeRoundNumber}");

            if (!entries.Any())
            {
                logger.Warn($"ConsolidateActiveVotes found no active entries, announcing and switching to standby");

                await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughEntriesAnnouncement,
                    pin: true);

                return Tuple.Create<FinalizationResult, User>(FinalizationResult.NotEnoughContesters,null);
            }

            var winnersGroup = entries.GroupBy(e => e.ConsolidatedVoteCount ?? 0).OrderByDescending(g => g.Key)
                .FirstOrDefault();

            //wtf, not expected
            if (winnersGroup == null)
            {
                logger.Error("WinnersGroup is null, not expected");
                return Tuple.Create<FinalizationResult, User>(FinalizationResult.Halt,null);
            }

            var voteCount = winnersGroup?.Key ?? 0;

            if (voteCount < _botConfiguration.MinAllowedVoteCountForWinners)
            {
                logger.Warn(
                    $"Winners got {winnersGroup?.Key} votes total, that's less than _configuration.MinAllowedVoteCountForWinners ({_botConfiguration.MinAllowedVoteCountForWinners}), " +
                    $"announcing and switching to standby");

                await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughVotesAnnouncement, true,
                    Tuple.Create(LocTokens.VoteCount, voteCount.ToString()));
                
                return Tuple.Create<FinalizationResult, User>(FinalizationResult.NotEnoughVotes,null);
            }

            //OK, we got valid winner(s)

            var winnerDictionary = new Dictionary<ActiveContestEntry, User>();

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

                return Tuple.Create<FinalizationResult, User>(FinalizationResult.Halt,null);                
            }

            User actualWinner = null;
            ActiveContestEntry winningEntry = null;

            if (winnerDictionary.Count == 1)
            {
                //we have 1 winner, that's easy: just announce him

                var pair = winnerDictionary.FirstOrDefault();

                actualWinner = pair.Value;
                winningEntry = pair.Key;

                await _broadcastController.AnnounceInMainChannel(_loc.WeHaveAWinner, false,
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

                await _broadcastController.AnnounceInMainChannel(_loc.WeHaveWinners, false,
                    Tuple.Create(LocTokens.User, actualWinner.GetHtmlUserLink()),
                    Tuple.Create(LocTokens.Users, winnersList),
                    Tuple.Create(LocTokens.VoteCount, voteCount.ToString()));
            }

            //forward winner's entry to main channel
            state = _repository.GetOrCreateCurrentState();

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

            _repository.UpdateState(s=>s.CurrentWinnerId, actualWinner.Id);

            return Tuple.Create<FinalizationResult, User>(FinalizationResult.Ok,actualWinner);
        }

        private async Task CreateVotingControlsForEntry(ActiveContestEntry activeEntry)
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

        private List<InlineKeyboardButton> CreateVotingButtonsForEntry(ActiveContestEntry activeEntry)
        {
            var inlineKeyboardButtons = new List<InlineKeyboardButton>();

            for (int i = _botConfiguration.MinVoteValue; i <= _botConfiguration.MaxVoteValue; i++)
            {
                var smileIndex = i - _botConfiguration.MinVoteValue;

                inlineKeyboardButtons.Add(InlineKeyboardButton.WithCallbackData(
                    smileIndex < _votingSmiles.Length ? _votingSmiles[smileIndex]: i.ToString(),
                    CreateQueryDataForEntryAndValue(i, activeEntry)));
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

        private string CreateQueryDataForEntryAndValue(int i, ActiveContestEntry activeEntry) => 
            CommandManager.ConstructQueryData(this, $"{i}{QuerySeparatorChar}{activeEntry.Id}");
    }
}