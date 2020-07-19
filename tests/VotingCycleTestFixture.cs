using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using NUnit.Framework;
using tests.DI;
using tests.Mockups;

namespace tests
{
    [TestFixture]
    public class VotingCycleTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(VotingCycleTestFixture));

        [Test]
        public async Task ShouldSwitchToStandbyWhenNotEnoughContestEntries()
        {
            using (var compartment = new TestCompartment())
            {
                //Setup

                await compartment.ScenarioController.
                    StartUserScenario(compartment.GenericScenarios.SupervisorKickstartContest,
                        UserCredentials.Supervisor).ScenarioTask;

                Assert.That(await compartment.WaitTillStateMatches(state => state.CurrentTaskMessagelId != null),
                    Is.True,
                    "Failed kickstarting contest (message id not set)");

                //Run generic users

                Logger.Info("Running 'submit' scenarios");

                var users = new[]
                {
                    compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario)
                };

                await Task.WhenAll(users.Select(u => u.ScenarioTask)); //wait for all user scenarios to complete

                //Set deadline to 'now'

                Logger.Info("Running 'set deadline' scenario");

                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    await compartment.GenericScenarios.SupervisorSetDeadlineToNow(context);

                    var warningMessage = await context.ReadTillMessageReceived(MockConfiguration.MainChat.Id);

                    var expectedWarningMessage = LocTokens.SubstituteTokens(compartment.Localization.
                            ContestDeadline_NotEnoughEntriesTemplateFinal,
                        Tuple.Create(
                            LocTokens.Time,
                            compartment.Localization.AlmostNothing));

                    Assert.That(warningMessage.Text, Contains.Substring(expectedWarningMessage),
                        "Didnt get preview warning message");

                    var standbyMessage = await context.ReadTillMessageReceived(MockConfiguration.MainChat.Id);

                    Assert.That(standbyMessage.Text,
                        Contains.Substring(compartment.Localization.NotEnoughEntriesAnnouncement));
                }, UserCredentials.Supervisor).ScenarioTask;

                Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Standby),
                    Is.True,
                    "Failed switching to Standby state after deadline hit");
            }
        }

        [Test]
        public async Task ShouldSwitchToVotingWhenEnoughContestEntries()
        {
            using (var compartment = new TestCompartment())
            {
                //Setup

                await compartment.ScenarioController.
                    StartUserScenario(compartment.GenericScenarios.SupervisorKickstartContest,
                        UserCredentials.Supervisor).ScenarioTask;

                Assert.That(await compartment.WaitTillStateMatches(state => state.CurrentTaskMessagelId != null),
                    Is.True,
                    "Failed kickstarting contest (message id not set)");

                //Run generic users

                Logger.Info("Running 'submit' scenarios");

                var users = new[]
                {
                    compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.
                        ContesterUserScenario),
                    compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.
                        ContesterUserScenario),
                    compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.
                        ContesterUserScenario)
                };

                await Task.WhenAll(users.Select(u => u.ScenarioTask)); //wait for all user scenarios to complete

                //Set deadline to 'now'

                var timeService = compartment.Container.Resolve<TimeService>();

                Logger.Info("Running 'set deadline' scenario");

                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    var oldDeadline = compartment.Repository.GetOrCreateCurrentState()?.NextDeadlineUTC;

                    await compartment.GenericScenarios.SupervisorSetDeadlineToNow(context);

                    var votingPinEdited =
                        await context.ReadTillMessageEdited(MockConfiguration.MainChat.Id, TimeSpan.FromSeconds(10));

                    var newDeadline = compartment.Repository.GetOrCreateCurrentState()?.NextDeadlineUTC;

                    Assert.That(oldDeadline, Is.Not.EqualTo(newDeadline), "Deadline not modified in SystemState");
                    Assert.That(newDeadline, Is.Not.Null, "Deadline not set in SystemState");

                    Assert.That(votingPinEdited.Text, Contains.Substring(timeService.
                            FormatDateAndTimeToAnnouncementTimezone(newDeadline.Value)),
                        "didnt modify voting message");

                    Logger.Info("Test: " +
                                "changed deadline from " +
                                $"{(oldDeadline == null ? "null" : timeService.FormatDateAndTimeToAnnouncementTimezone(oldDeadline.Value))})" +
                                " to " +
                                $"{timeService.FormatDateAndTimeToAnnouncementTimezone(newDeadline.Value)} ok!");
                }, UserCredentials.Supervisor).ScenarioTask;


                Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting),
                    Is.True, "Failed switching to Voting state after deadline hit");
            }
        }
    }
}