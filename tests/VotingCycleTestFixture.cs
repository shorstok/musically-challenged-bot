using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Dapper;
using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using NUnit.Framework;
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
        public async Task ShouldDenySumbissionsInNonContestStates()
        {
            var deniedStates = new[]
            {
                ContestState.Standby,
                ContestState.ChoosingNextTask,
                ContestState.FinalizingVotingRound,
                ContestState.InnerCircleVoting,
                ContestState.Voting
            };

            using (var compartment = new TestCompartment())
            {
                foreach (var contestState in deniedStates)
                {
                    compartment.Repository.UpdateState(state => state.State, contestState);

                    await compartment.ScenarioController.StartUserScenario(async context =>
                    {
                        context.SendCommand(Schema.SubmitCommandName);

                        var answer = await context.ReadTillMessageReceived(context.PrivateChat.Id);

                        Assert.That(answer?.Text, Contains.Substring(context.Localization.
                                SubmitContestEntryCommandHandler_OnlyAvailableInContestState),
                            "/submit command response should be 'denied' message");

                        Logger.Info($"Submission denied in {contestState} state - OK");
                    }).ScenarioTask;
                }
            }
        }

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

                Assert.That(compartment.Repository.ConsolidateVotesForActiveEntriesGetAffected().Count(),
                    Is.EqualTo(0),
                    "Failed to close active entries when switched to standby");
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
                        "didn't modify voting message");

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

        [Test]
        public async Task ShouldWinMostVotedUser()
        {
            using (var compartment = new TestCompartment())
            {
                //Setup

                var mediator = compartment.Container.Resolve<MockMessageMediatorService>();
                var votingController = compartment.Container.Resolve<VotingController>();

                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToArray();

                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting),
                        Is.True, "Failed switching to Voting state after deadline hit");

                    await context.ReadTillMessageReceived(mock =>
                        mock.ChatId.Identifier == MockConfiguration.VotingChat.Id &&
                        mock.Text.Contains(context.Localization.VotingStatsHeader));

                    //Check that system created voting buttons markup on voting start

                    foreach (var votingEntity in votingEntities)
                    {
                        var votingMessage =
                            mediator.GetMockMessage(votingEntity.Item2.Chat.Id, votingEntity.Item2.MessageId);

                        Assert.That(votingMessage.ReplyMarkup?.InlineKeyboard?.SelectMany(buttons => buttons)?.Count(),
                            Is.EqualTo(5),
                            $"Didn't create five voting buttons for entry {votingEntity.Item2.Text}");
                    }
                }).ScenarioTask;

                //Vote for entry 1 with 5 users with max vote

                var voterCount = 5;

                for (var nuser = 0; nuser < voterCount; nuser++)
                {
                    await compartment.ScenarioController.StartUserScenario(context =>
                    {
                        var maxVoteValue = votingController.VotingSmiles.Max(x => x.Key);
                        var maxVoteSmile = votingController.VotingSmiles[maxVoteValue];
                        var button = votingEntities[1].Item2.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                            FirstOrDefault(b => b.Text == maxVoteSmile);

                        Assert.That(button, Is.Not.Null,
                            $"Max voting value button (labelled {maxVoteSmile}) not found in reply markup");

                        context.SendQuery(button.CallbackData, votingEntities[1].Item2);
                    }).ScenarioTask;
                }

                //Ffwd voting

                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    await compartment.GenericScenarios.SupervisorSetDeadlineToNow(context);

                    var votingPinEdited =
                        await context.ReadTillMessageEdited(MockConfiguration.MainChat.Id, TimeSpan.FromSeconds(10));
                }, UserCredentials.Supervisor).ScenarioTask;

                Assert.That(await compartment.WaitTillStateMatches(state =>
                        state.State == ContestState.ChoosingNextTask || state.State == ContestState.Contest),
                    Is.True, "Failed switching to ChoosingNextTask or Contest state after deadline hit");

                //Ensure author for entry 1 won

                Assert.That(
                    compartment.Repository.GetOrCreateCurrentState().CurrentWinnerId,
                    Is.EqualTo(votingEntities[1].Item1.From.Id),
                    "Wrong user won");

                //Ensure votes calculated correctly

                using (var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository))
                {
                    var winnerId = votingEntities[1].Item1.From.Id;

                    var winnerVoteCount = connection.Query<decimal?>(
                            @"select ConsolidatedVoteCount from ActiveContestEntry where AuthorUserId = @UserId",
                            new {UserId = winnerId}).
                        FirstOrDefault();

                    Assert.That(winnerVoteCount,
                        Is.EqualTo(voterCount * MockConfiguration.Snapshot.MaxVoteValue),
                        "Wrong vote sum");
                    
                    var otherVotes = connection.Query<decimal?>(
                            @"select ConsolidatedVoteCount from ActiveContestEntry where AuthorUserId != @UserId",
                            new {UserId = winnerId}).ToArray();

                    var averageVoteValue = votingController.GetDefaultVoteForUser(new User {Id = 0xffffff});

                    Assert.That(otherVotes,
                        Is.All.EqualTo(voterCount * averageVoteValue),
                        "Wrong vote sum for non-winners");
                }
            }
        }       
        
        [Test]
        public async Task ShouldFallbackToTaskPollOnSuggestionTimeout()
        {
            using (var compartment = new TestCompartment())
            {
                //Setup
                
                //Force immediate timeout on task selection
                compartment.Configuration.MaxTaskSelectionTimeHours = 0;

                var mediator = compartment.Container.Resolve<MockMessageMediatorService>();
                var votingController = compartment.Container.Resolve<VotingController>();

                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToArray();

                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting),
                        Is.True, "Failed switching to Voting state after deadline hit");

                    await context.ReadTillMessageReceived(mock =>
                        mock.ChatId.Identifier == MockConfiguration.VotingChat.Id &&
                        mock.Text.Contains(context.Localization.VotingStatsHeader));

                    //Check that system created voting buttons markup on voting start

                    foreach (var votingEntity in votingEntities)
                    {
                        var votingMessage =
                            mediator.GetMockMessage(votingEntity.Item2.Chat.Id, votingEntity.Item2.MessageId);

                        Assert.That(votingMessage.ReplyMarkup?.InlineKeyboard?.SelectMany(buttons => buttons)?.Count(),
                            Is.EqualTo(5),
                            $"Didn't create five voting buttons for entry {votingEntity.Item2.Text}");
                    }
                }).ScenarioTask;

                //Vote for entry 1 with 5 users with max vote

                var voterCount = 5;

                for (var nuser = 0; nuser < voterCount; nuser++)
                {
                    await compartment.ScenarioController.StartUserScenario(context =>
                    {
                        var maxVoteValue = votingController.VotingSmiles.Max(x => x.Key);
                        var maxVoteSmile = votingController.VotingSmiles[maxVoteValue];
                        var button = votingEntities[1].Item2.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                            FirstOrDefault(b => b.Text == maxVoteSmile);

                        Assert.That(button, Is.Not.Null,
                            $"Max voting value button (labelled {maxVoteSmile}) not found in reply markup");

                        context.SendQuery(button.CallbackData, votingEntities[1].Item2);
                    }).ScenarioTask;
                }

                //Ffwd voting

                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    await compartment.GenericScenarios.SupervisorSetDeadlineToNow(context);

                    var votingPinEdited =
                        await context.ReadTillMessageEdited(MockConfiguration.MainChat.Id, TimeSpan.FromSeconds(10));
                }, UserCredentials.Supervisor).ScenarioTask;

                Assert.That(await compartment.WaitTillStateMatches(state =>
                        state.State == ContestState.TaskSuggestionCollection || state.State == ContestState.Contest),
                    Is.True, "Failed switching to TaskSuggestionCollection on task selection timeout");

                
            }
        }
    }
}