using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using NodaTime;
using NUnit.Framework;
using Telegram.Bot.Types;

namespace tests.Mockups
{
    public class GenericUserScenarios
    {
        private static readonly ILog Logger = Log.Get(typeof(GenericUserScenarios));
        private readonly IClock _clock;
        private readonly LocStrings _localization;

        public GenericUserScenarios(IClock clock, LocStrings localization)
        {
            _clock = clock;
            _localization = localization;
        }

        /// <summary>
        ///     Issues commands as supervisor to set next deadline to 'now'
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task SupervisorSetDeadlineToNow(UserScenarioContext context)
        {
            context.SendCommand(Schema.DeadlineCommandName);

            var prompt = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            Assert.That(prompt.Text, Contains.Substring("Confirm"),
                $"Didn't get deadline confirmation in chat {context.PrivateChat.Id}");
            Assert.That(prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.Any(),
                Is.True,
                $"/{Schema.DeadlineCommandName} didnt send confirmation buttons in reply");

            var yesButton = prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                FirstOrDefault(b => b.Text == "YES");

            Assert.That(yesButton, Is.Not.Null, "'yes' button not found in query answer");

            context.SendQuery(yesButton.CallbackData, prompt);

            var response = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            Assert.That(response.Text, Contains.Substring("Confirmed"), "didnt get deadline confirmation ack");

            await context.ReadTillMessageReceived(context.PrivateChat.Id); //read description

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
        ///     Executes commands to run contest from Standby phase
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task SupervisorKickstartContest(UserScenarioContext context)
        {
            context.SendCommand(Schema.KickstartCommandName);

            var prompt = await context.ReadTillMessageReceived();

            Assert.That(prompt.Text, Contains.Substring("send task template"));

            context.SendMessage("task template!", context.PrivateChat);

            var response = await context.ReadTillMessageReceived(context.PrivateChat.Id);

            Assert.That(response.Text, Contains.Substring("all OK"));

            Logger.Info("Test: contest kickstarted ok!");

            var announce = await context.ReadTillMessageReceived(mock =>
                mock.ChatId.Identifier == MockConfiguration.MainChat.Id &&
                mock.Text.Contains("template!"));

            Assert.That(announce, Is.Not.Null);

            Logger.Info("Got task template in main chat");
        }

        /// <summary>
        ///     Submits fake file to challenge bot as contest entry
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

            context.SendAudioFile(new Audio {FileSize = 10000, Title = fakeFileTitle}, context.PrivateChat);

            var forwrdedMessage = await context.ReadTillMessageForwardedEvent(mock =>
                mock.ChatId.Identifier == MockConfiguration.VotingChat.Id &&
                mock.FromChatId.Identifier == context.PrivateChat.Id);

            Assert.That(forwrdedMessage, Is.Not.Null, "Contest entry was not forwarded");
            Assert.That(forwrdedMessage.Audio, Is.Not.Null, "Contest entry has no audiofile");
            Assert.That(forwrdedMessage.Audio.Title, Is.EqualTo(fakeFileTitle), "Contest entry audio mismatch");
        }
    }
}