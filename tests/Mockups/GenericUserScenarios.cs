using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using NodaTime;
using NUnit.Framework;
using Telegram.Bot.Types;
using tests.Mockups.Messaging;

namespace tests.Mockups
{
    public class GenericUserScenarios
    {
        private readonly IClock _clock;
        private readonly MockTelegramClient _telegramClient;
        private readonly LocStrings _localization;
        private readonly IRepository _repository;
        private readonly MockMessageMediatorService _messageMediator;

        private static readonly ILog Logger = Log.Get(typeof(GenericUserScenarios));

        public GenericUserScenarios(IClock clock, 
            MockTelegramClient telegramClient,
            LocStrings localization, 
            IRepository repository,
            MockMessageMediatorService messageMediator)
        {
            _clock = clock;
            _telegramClient = telegramClient;
            _localization = localization;
            _repository = repository;
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

                var user = new Telegram.Bot.Types.User{Id = userid, Username = $"fake user {userid}"};
                var message = new Message
                {
                    Chat = new Chat{Id = MockConfiguration.VotingChat.Id},
                    MessageId = msgid,
                    From = user,
                    Text = $"Fake contest entry {i}",
                    Audio = new Audio{FileSize = 10, Title = $"Fake contest entryfile"}
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

                _repository.GetOrCreateContestEntry(_repository.CreateOrGetUserByTgIdentity(user),
                    message.Chat.Id, message.MessageId, votingControlsContainerMessage.MessageId,
                    currentState.CurrentChallengeRoundNumber, out var previous);

                yield return Tuple.Create(message, votingControlsContainerMessage);
            }

            _repository.UpdateState(state => state.NextDeadlineUTC,
                _clock.GetCurrentInstant());

            _repository.UpdateState(state => state.State, ContestState.Contest);

            Logger.Info($"Voting cycle set with {submissionCount} submissions");
        }
    }
}
