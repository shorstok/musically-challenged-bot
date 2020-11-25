using System;
using System.Collections.Generic;
using System.Globalization;
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
using musicallychallenged.Services;
using NodaTime;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using tests.DI;
using tests.Mockups.Messaging;

namespace tests.Mockups
{
    public class GenericUserScenarios
    {
        private readonly IClock _clock;
        private readonly MockTelegramClient _telegramClient;
        private readonly LocStrings _localization;
        private readonly IRepository _repository;
        private readonly BotConfiguration _configuration;
        private readonly MockMessageMediatorService _messageMediator;

        private static readonly ILog Logger = Log.Get(typeof(GenericUserScenarios));

        public GenericUserScenarios(IClock clock, 
            MockTelegramClient telegramClient,
            LocStrings localization, 
            IRepository repository,
            BotConfiguration configuration,
            MockMessageMediatorService messageMediator)
        {
            _clock = clock;
            _telegramClient = telegramClient;
            _localization = localization;
            _repository = repository;
            _configuration = configuration;
            _messageMediator = messageMediator;
        }

        /// <summary>
        /// Issues commands as supervisor to set next deadline
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task SupervisorSetDeadlineToNow(UserScenarioContext context)
        {
            context.SendCommand(Schema.DeadlineCommandName);

            var prompt = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            Assert.That(prompt.Text, Contains.Substring("Confirm"), $"Didn't get deadline confirmation in chat {context.PrivateChat.Id}");
            Assert.That(prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.Any(),
                Is.True,
                $"/{Schema.DeadlineCommandName} didnt send confirmation buttons in reply");

            var yesButton = prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                FirstOrDefault(b => b.Text == "YES");

            Assert.That(yesButton, Is.Not.Null, "'yes' button not found in query answer");

            context.SendQuery(yesButton.CallbackData, prompt);

            var response = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            Assert.That(response.Text, Contains.Substring("Confirmed"), "didnt get deadline confirmation ack");

            await context.ReadTillMessageReceived(context.PrivateChat.Id);  //read description

            var zonedDateTime = _clock.GetCurrentInstant().
                InZone(DateTimeZoneProviders.Tzdb[MockConfiguration.Snapshot.AnnouncementTimeZone]);

            var preNow = zonedDateTime.
                ToString("dd.MM.yy HH:mm", CultureInfo.InvariantCulture);

            context.SendMessage(preNow, context.PrivateChat);

            //Second confirmation

            prompt = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            Assert.That(prompt.Text, Contains.Substring("Confirm"), "Didn't get deadline confirmation");
            Assert.That(prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.Any(),
                Is.True,
                $"/{Schema.DeadlineCommandName} didnt send confirmation buttons in reply");

            yesButton = prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                FirstOrDefault(b => b.Text == "YES");

            Assert.That(yesButton, Is.Not.Null, "'yes' button not found in query answer");

            context.SendQuery(yesButton.CallbackData, prompt);

            Logger.Info($"Test: set deadline to {preNow}/{MockConfiguration.Snapshot.AnnouncementTimeZone} ok!");
        }

        /// <summary>
        /// Executes commands to run contest from Standby phase
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task SupervisorKickstartContest(UserScenarioContext context)
        {
            context.SendCommand(Schema.KickstartCommandName);

            var prompt = await context.ReadTillMessageReceived();

            Assert.That(prompt.Text, Contains.Substring("send task template"));

            context.SendMessage($"task template!", context.PrivateChat);

            var response = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            Assert.That(response.Text, Contains.Substring("all OK"));

            Logger.Info($"Test: contest kickstarted ok!");

            var announce = await context.ReadTillMessageReceived(mock =>
                mock.ChatId.Identifier == MockConfiguration.MainChat.Id &&
                mock.Text.Contains("template!"));

            Assert.That(announce, Is.Not.Null);

            Logger.Info($"Got task template in main chat");
        }


        
        /// <summary>
        /// Issues commands as supervisor to set next deadline
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> PostponeUserScenario(UserScenarioContext context,
            Func<IEnumerable<InlineKeyboardButton>,InlineKeyboardButton> choice)
        {
            context.SendCommand(Schema.PostponeCommandName);

            var prompt = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            Assert.That(prompt.Text, Contains.Substring(_localization.PostponeCommandHandler_Preamble),
                $"Didn't get postpone preamble in chat {context.PrivateChat.Id}");

            Assert.That(prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.Any(),
                Is.True,
                $"/{Schema.PostponeCommandName} didnt send control buttons in reply");


            var options = (prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault() ?? new InlineKeyboardButton[0]).ToArray();

            var selectedButton = choice(options);

            Assert.That(selectedButton, Is.Not.Null, $"No postpone button selected in 'choice' handler, " +
                                                     $"available options were {string.Join(", ",options.Select(op=>op.Text))}");

            context.SendQuery(selectedButton.CallbackData, prompt);

            var response = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            var possibleAcks = new[]
            {
                _localization.PostponeCommandHandler_AcceptedTemplate,
                _localization.PostponeCommandHandler_AcceptedPostponedTemplate,
                _localization.PostponeCommandHandler_Cancelled,
                _localization.PostponeCommandHandler_DeniedAlreadyHasOpenTemplate,
                _localization.PostponeCommandHandler_DeniedNoQuotaLeftTemplate,
                _localization.PostponeCommandHandler_OnlyForKnownUsers,
            }.Select(s => LocTokens.SubstituteTokens(s,
                Tuple.Create(LocTokens.Users, _configuration.PostponeQuorum.ToString()),
                Tuple.Create(LocTokens.Time, _configuration.PostponeHoursAllowed.ToString("0."))
            )).ToArray();

            Assert.That(
                possibleAcks.Contains(response.Text.Trim()), 
                Is.True, 
                $"Got reply `{response.Text}` -- invalid postpone ack");

            return response.Text;
        }


        /// <summary>
        /// Submits fake file to challenge bot as contest entry
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task ContesterUserScenario(UserScenarioContext context)
        {
            context.SendCommand(Schema.SubmitCommandName);

            var answer = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            Assert.That(answer?.Text, Contains.Substring(context.Localization.
                    SubmitContestEntryCommandHandler_SubmitGuidelines),
                "/submit command response should contain general submit pretext");

            var fakeFileTitle = Guid.NewGuid().ToString();

            context.SendAudioFile(new Audio { FileSize = 10000, Title = fakeFileTitle }, context.PrivateChat);

            var forwrdedMessage = await context.ReadTillMessageForwardedEvent(mock =>
                mock.ChatId.Identifier == MockConfiguration.VotingChat.Id &&
                mock.FromChatId.Identifier == context.PrivateChat.Id);

            Assert.That(forwrdedMessage, Is.Not.Null, "Contest entry was not forwarded");
            Assert.That(forwrdedMessage.Audio, Is.Not.Null, "Contest entry has no audiofile");
            Assert.That(forwrdedMessage.Audio.Title, Is.EqualTo(fakeFileTitle), "Contest entry audio mismatch");

            //Should get 'all ok' message

            var ackMessage = await context.ReadTillMessageReceived(mock =>
                mock.ChatId.Identifier == context.PrivateChat.Id &&
                mock.Text.Contains(_localization.SubmitContestEntryCommandHandler_SubmissionSucceeded));
        }

  
        public async Task<int?> FinishContestAndSimulateVoting(TestCompartment compartment)
        {
            var entries = _repository.GetActiveContestEntries().ToArray();

            if (entries.Length == 0)
            {
                Logger.Error($"Cant simulate voting - no contest entries present! Nothing to vote for.");
                return null;
            }

            _repository.UpdateState(state => state.State, ContestState.Contest);

            _repository.UpdateState(state => state.NextDeadlineUTC,
                _clock.GetCurrentInstant());

            await compartment.ScenarioController.StartUserScenario(async context =>
            {
                Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting),
                    Is.True, "Failed switching to Voting state after deadline hit");

                await context.ReadTillMessageReceived(mock =>
                    mock.ChatId.Identifier == MockConfiguration.VotingChat.Id &&
                    mock.Text.Contains(context.Localization.VotigStatsHeader));

                //Check that system created voting buttons markup on voting start

                foreach (var contestEntry in entries)
                {
                    var votingMessage =
                        _messageMediator.GetMockMessage(contestEntry.ContainerChatId, contestEntry.ContainerMesssageId);

                    Assert.That(votingMessage.ReplyMarkup?.InlineKeyboard?.SelectMany(buttons => buttons)?.Count(),
                        Is.EqualTo(5),
                        $"Didnt create five voting buttons for entry {contestEntry.Id}");
                }
            }).ScenarioTask;

            //Vote for entry 1 with 5 users with max vote

            var voterCount = 5;

            var targetVotingMessage =
                _messageMediator.GetMockMessage(entries[0].ContainerChatId, entries[0].ContainerMesssageId);


            for (var nuser = 0; nuser < voterCount; nuser++)
            {
                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    var maxVoteSmile = VotingController._votingSmiles.Last();
                    var button = targetVotingMessage.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                        FirstOrDefault(b => b.Text == maxVoteSmile);

                    Assert.That(button, Is.Not.Null,
                        $"Max voting value button (labelled {maxVoteSmile}) not found in reply markup");

                    context.SendQuery(button.CallbackData, targetVotingMessage);
                }).ScenarioTask;
            }

            //Ffwd voting

            _repository.UpdateState(state => state.NextDeadlineUTC,
                _clock.GetCurrentInstant());

            await CompleteTaskSelectionAsWinner(compartment, entries[0].AuthorUserId);

            return entries[0].AuthorUserId;

        }

        public async Task CompleteTaskSelectionAsWinner(TestCompartment compartment, int winnerId)
        {
            await compartment.ScenarioController.StartUserScenarioForExistingUser(
                winnerId,
                async winnerCtx =>
                {
                    Assert.That(await compartment.WaitTillStateMatches(state =>
                            state.State == ContestState.ChoosingNextTask || state.State == ContestState.Contest),
                        Is.True, "Failed switching to ChoosingNextTask or Contest state after deadline hit");

                    //Ensure author for entry 1 won

                    Assert.That(
                        compartment.Repository.GetOrCreateCurrentState().CurrentWinnerId,
                        Is.EqualTo(winnerCtx.MockUser.Id),
                        "Wrong user won");

                    //Winner posts new round task description

                    await winnerCtx.ReadTillPrivateMessageReceived(msg =>
                            msg.Text == compartment.Localization.CongratsPrivateMessage,
                        TimeSpan.FromSeconds(1));

                    var messageWithControls = await winnerCtx.ReadTillPrivateMessageReceived(msg =>
                            msg.Text == compartment.Localization.ChooseNextRoundTaskPrivateMessage,
                        TimeSpan.FromSeconds(1));

                    Assert.That(messageWithControls.ReplyMarkup.InlineKeyboard.Count(), Is.EqualTo(1),
                        "Winner task selector should have 1 reply button (for random task selection)");

                    winnerCtx.SendMessage("mock task", winnerCtx.PrivateChat);

                    Logger.Info($"Completed task selection as winner");

                }).ScenarioTask;

        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Tuple<Message,Message>> PrepareVotingCycle(int submissionCount)
        {
            if(submissionCount < 1)
                throw new ArgumentException("Submission count should be greater than 0", nameof(submissionCount));

            var currentState = _repository.GetOrCreateCurrentState();

            //Seed mock voting channel with some entries

            for (int i = 0; i < submissionCount; i++)
            {
                var userid = MockConfiguration.GetNewMockUserId();
                var msgid = MockConfiguration.CreateNewMockMessageId();
                var controlsMsgid = MockConfiguration.CreateNewMockMessageId();

                var user = new Telegram.Bot.Types.User
                {
                    Id = userid,
                    Username = $"fakeusr-{userid}",
                    FirstName = $"Contest Entry#{i} Author"
                };

                var message = new Message
                {
                    Chat = new Chat { Id = MockConfiguration.VotingChat.Id },
                    MessageId = msgid,
                    From = user,
                    Text = $"Fake contest entry {i}",
                    Audio = new Audio { FileSize = 10, Title = $"Fake contest entryfile" }
                };

                var votingControlsContainerMessage = new Message
                {
                    Chat = new Chat{Id = MockConfiguration.VotingChat.Id},
                    MessageId = controlsMsgid,
                    From = MockConfiguration.MockBotUser,
                    Text = $"description for entry {i}",
                };

                _messageMediator.InsertMockMessage(message);
                _messageMediator.InsertMockMessage(votingControlsContainerMessage);

                var mockUser = _repository.CreateOrGetUserByTgIdentity(user);

                _repository.GetOrCreateContestEntry(mockUser,
                    message.Chat.Id, message.MessageId, votingControlsContainerMessage.MessageId,
                    currentState.CurrentChallengeRoundNumber, out var previous);

                _repository.UpdateUser(mockUser, MockConfiguration.CreateNewPrivateChatId());

                yield return Tuple.Create(message, votingControlsContainerMessage);
            }

            _repository.UpdateState(state => state.NextDeadlineUTC,
                _clock.GetCurrentInstant());

            _repository.UpdateState(state => state.State, ContestState.Contest);

            Logger.Info($"Voting cycle set with {submissionCount} submissions");
        }
    }
}
