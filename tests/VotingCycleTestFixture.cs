using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using musicallychallenged.Services.Telegram;
using NodaTime;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using tests.DI;
using tests.Mockups;
using tests.Mockups.Messaging;

namespace tests
{
    [TestFixture]
    public class VotingCycleTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(VotingCycleTestFixture));

        [Test]
        public async Task ShouldSwitchToVotingWhenEnoughContestEntries()
        {
            using (var compartment = new MockupTgCompartment())
            {
                async Task ContesterUserScenario(UserScenarioContext context)
                {
                    context.SendMessage("/submit", context.PrivateChat);

                    var answer = await context.ReadTillMessageReceived(context.PrivateChat.Id);

                    Assert.That(answer?.Text, Contains.Substring(compartment.Localization.
                            SubmitContestEntryCommandHandler_SubmitGuidelines),
                        "/submit command response should contain general submit pretext");

                    var fakeFileTitle = Guid.NewGuid().ToString();

                    context.SendAudioFile(new Audio{FileSize = 10000, Title = fakeFileTitle }, context.PrivateChat);
                    
                    var forwrdedMessage = await context.ReadTillMessageForwardedEvent(mock =>
                        mock.ChatId.Identifier == MockConfiguration.VotingChat.Id &&
                        mock.FromChatId.Identifier == context.PrivateChat.Id);

                    Assert.That(forwrdedMessage,Is.Not.Null, "Contest entry was not forwarded");
                    Assert.That(forwrdedMessage.Audio,Is.Not.Null, "Contest entry has no audiofile");
                    Assert.That(forwrdedMessage.Audio.Title,Is.EqualTo(fakeFileTitle), "Contest entry audio mismatch");


                }
                
                //Setup

                await compartment.StartUserScenario(async context =>
                {
                    context.SendMessage($"/{Scheme.KickstartCommandName}", context.PrivateChat);

                    var prompt = await context.ReadTillMessageReceived();

                    Assert.That(prompt.Text, Contains.Substring("send task template"));

                    context.SendMessage($"task template!", context.PrivateChat);

                    var response = await context.ReadTillMessageReceived(context.PrivateChat.Id);

                    Assert.That(response.Text, Contains.Substring("all OK"));
                }, UserCredentials.Supervisor).ScenarioTask;

                //Run generic users

                var users = new[]
                {
                    compartment.StartUserScenario(ContesterUserScenario),
                    compartment.StartUserScenario(ContesterUserScenario),
                    compartment.StartUserScenario(ContesterUserScenario)
                };

                await Task.WhenAll(users.Select(u=>u.ScenarioTask)); //wait for all user scenarios to complete

                //Set deadline to 'now'

                var clock = compartment.Container.Resolve<IClock>();
                var timeService  = compartment.Container.Resolve<TimeService>();
                
                await compartment.StartUserScenario(async context =>
                {
                    context.SendMessage($"/{Scheme.DeadlineCommandName}", context.PrivateChat);

                    var prompt = await context.ReadTillMessageReceived();

                    Assert.That(prompt.Text, Contains.Substring("Confirm"), "Didn't get deadline confirmation");
                    Assert.That(prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.Any(),
                        Is.True,
                        $"/{Scheme.DeadlineCommandName} didnt send confirmation buttons in reply");

                    var yesButton = prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                        FirstOrDefault(b => b.Text == "YES");

                    Assert.That(yesButton, Is.Not.Null, "'yes' button not found in query answer");

                    context.SendQuery(yesButton.CallbackData,prompt);

                    var response = await context.ReadTillMessageReceived(context.PrivateChat.Id);

                    Assert.That(response.Text, Contains.Substring("Confirmed"), "didnt get deadline confirmation ack");

                    await context.ReadTillMessageReceived(context.PrivateChat.Id);  //read description

                    var zonedDateTime = clock.GetCurrentInstant().
                        InZone(DateTimeZoneProviders.Tzdb[MockConfiguration.Snapshot.AnnouncementTimeZone]);

                    var preNow = zonedDateTime.
                        ToString("dd.MM.yy HH:mm",CultureInfo.InvariantCulture);

                    context.SendMessage(preNow, context.PrivateChat);

                    //Second confirmation

                    prompt = await context.ReadTillMessageReceived();

                    Assert.That(prompt.Text, Contains.Substring("Confirm"), "Didn't get deadline confirmation");
                    Assert.That(prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.Any(),
                        Is.True,
                        $"/{Scheme.DeadlineCommandName} didnt send confirmation buttons in reply");

                    yesButton = prompt.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                        FirstOrDefault(b => b.Text == "YES");

                    Assert.That(yesButton, Is.Not.Null, "'yes' button not found in query answer");
                    
                    context.SendQuery(yesButton.CallbackData,prompt);

                    //Ensure contest message now has updated date

                    var votingPinEdited = await context.ReadTillMessageEdited(MockConfiguration.MainChat.Id,TimeSpan.FromSeconds(10));
                    
                    Assert.That(votingPinEdited.Text, Contains.Substring(timeService.FormatDateAndTimeToAnnouncementTimezone(zonedDateTime.ToInstant())),
                        "didnt modify voting message");

                }, UserCredentials.Supervisor).ScenarioTask;


            }
        }
    }
}