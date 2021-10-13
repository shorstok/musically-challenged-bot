using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged.Domain;
using musicallychallenged.Logging;
using musicallychallenged.Services.Sync;
using musicallychallenged.Services.Sync.DTO;
using NUnit.Framework;
using tests.DI;
using tests.Mockups;

namespace tests
{
    [TestFixture]
    public class PesnocloudTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(PesnocloudTestFixture));

        [Test]
        public async Task ShouldIngestTracks()
        {
            using var compartment = new TestCompartment(TestContext.CurrentContext);

            var scenarioStartDate = DateTime.UtcNow;

            async Task SubmitAndUpdateEntry(UserScenarioContext context, string description)
            {
                //Submit entity
                await compartment.GenericScenarios.ContesterUserScenario(context);

                //Update description
                await compartment.GenericScenarios.ContesterUpdateDescription(context, description);
            }


            var syncService = compartment.Container.Resolve<SyncService>();
            var ingestService = compartment.Container.Resolve<MockIngestService>();

            //Setup

            await compartment.ScenarioController.StartUserScenario(
                compartment.GenericScenarios.SupervisorKickstartContest,
                UserCredentials.Supervisor).ScenarioTask;

            Assert.That(await compartment.WaitTillStateMatches(state => state.CurrentTaskMessagelId != null, false),
                Is.True, "Failed kickstarting contest (message id not set)");

            //Run generic users

            Logger.Info("Running 'submit' scenarios");

            var users = new[]
            {
                compartment.ScenarioController.StartUserScenario(ctx => SubmitAndUpdateEntry(ctx, "describe-first")),
                compartment.ScenarioController.StartUserScenario(ctx => SubmitAndUpdateEntry(ctx, "describe-second")),
            };

            await Task.WhenAll(users.Select(u => u.ScenarioTask)); //wait for all user scenarios to complete

            await ingestService.WaitTillQueueIngested(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

            Assert.That(ingestService.Tracks.Count, Is.EqualTo(2), "Expected exactly 2 tracks ingested");
            Assert.That(ingestService.Rounds.Count, Is.EqualTo(1), "Expected exactly 1 rounds ingested");

            var round = ingestService.Rounds.Values.Single();

            Assert.That(round.Id, Is.GreaterThan(0), "Round id not ingested");
            Assert.That(round.Source, Is.EqualTo("challenged"));
            Assert.That(round.Title, Is.Not.Null.Or.Empty);
            Assert.That(round.TaskText, Is.Not.Null.Or.Empty);
            Assert.That(round.EndDate, Is.GreaterThanOrEqualTo(scenarioStartDate));
            Assert.That(round.StartDate,
                Is.GreaterThanOrEqualTo(scenarioStartDate).And.LessThanOrEqualTo(DateTime.UtcNow));
            Assert.That(round.EndDate, Is.GreaterThan(round.StartDate));
            Assert.That(round.State, Is.EqualTo(BotContestRoundState.Open));

            var tracks = ingestService.Tracks.Values;

            Assert.That(tracks.All(t => t.AuthorId != null), "AuthorID not ingested");
            Assert.That(tracks.All(t => t.Id != 0), "Id not ingested");
            Assert.That(tracks.All(t => t.Source == "challenged"), "Invalid ingest source");
            Assert.That(tracks.All(t => t.Description.StartsWith("describe-")), "Description not ingested");
            Assert.That(tracks.All(t => t.Title.StartsWith("fake!")), "file Title not ingested");
            Assert.That(tracks.All(t => t.RoundId == round.Id), "Round id not ingested");

            foreach (var track in tracks)
                Assert.That(track.SubmissionDate,
                    Is.GreaterThanOrEqualTo(scenarioStartDate).And.LessThanOrEqualTo(DateTime.UtcNow));
        }


        [Test]
        public async Task ShouldIngestCloseRoundEvent()
        {
            using var compartment = new TestCompartment(TestContext.CurrentContext);

            var syncService = compartment.Container.Resolve<SyncService>();
            var ingestService = compartment.Container.Resolve<MockIngestService>();

            //Setup

            await compartment.ScenarioController.StartUserScenario(
                compartment.GenericScenarios.SupervisorKickstartContest,
                UserCredentials.Supervisor).ScenarioTask;

            Assert.That(await compartment.WaitTillStateMatches(state => state.CurrentTaskMessagelId != null, true),
                Is.True, "Failed kickstarting contest (message id not set)");

            //Run generic users

            Logger.Info("Running 'submit' scenarios");

            var users = new[]
            {
                compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario),
                compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario),
                compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario),
                compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario),
            };

            await Task.WhenAll(users.Select(u => u.ScenarioTask)); //wait for all user scenarios to complete

            //Ffwd contest

            await compartment.ScenarioController
                .StartUserScenario(
                    async context => { await compartment.GenericScenarios.SupervisorSetDeadlineToNow(context); },
                    UserCredentials.Supervisor).ScenarioTask;

            Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting, true),
                Is.True, "Failed switching to Voting state after deadline hit");

            //Check that there is 1 round and it is in the Voting state

            await ingestService.WaitTillQueueIngested(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

            Assert.That(ingestService.Rounds.Count, Is.EqualTo(1), "Expected exactly 1 rounds ingested");
            var firstRound = ingestService.Rounds.Values.Single();
            Assert.That(firstRound.State, Is.EqualTo(BotContestRoundState.Voting));

            //Add fake votes to produce a winner
            compartment.AddFakeVoteForEntries(entry => entry.Id + 1);

            // Check

            await ingestService.WaitTillQueueIngested(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

            Assert.That(firstRound.State, Is.EqualTo(BotContestRoundState.Voting));

            //Ffwd voting

            await compartment.ScenarioController.StartUserScenario(async context =>
            {
                await compartment.GenericScenarios.SupervisorSetDeadlineToNow(context);

                var votingPinEdited =
                    await context.ReadTillMessageEdited(MockConfiguration.MainChat.Id, TimeSpan.FromSeconds(10));
            }, UserCredentials.Supervisor).ScenarioTask;

            /* ensureTransitionEnded: false here because transition semaphore is locked till inner voting completes */
            Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.ChoosingNextTask,
                    ensureTransitionEnded: false),
                Is.True, "Failed switching to ChoosingNextTask state after deadline hit");

            var winner = compartment.GetCurrentWinnerDirect();

            await compartment.ScenarioController
                .StartUserScenario(context => { context.SendMessage("pseudotask #2!", context.PrivateChat); },
                    UserCredentials.User, useExistingUserId: winner.Id).ScenarioTask;

            Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Contest,
                    ensureTransitionEnded: true), Is.True,
                "Failed switching to ChoosingNextTask state after deadline hit");

            // Check

            await ingestService.WaitTillQueueIngested(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

            Assert.That(ingestService.Rounds.Count, Is.EqualTo(2), "Expected exactly 2 rounds ingested");
            firstRound = ingestService.Rounds[firstRound.Id];
            var secondRound = ingestService.Rounds.Values.Single(r => r.Id != firstRound.Id);
            Assert.That(firstRound.State, Is.EqualTo(BotContestRoundState.Closed));
            Assert.That(secondRound.State, Is.EqualTo(BotContestRoundState.Open));
            Assert.That(firstRound.TaskText, Is.EqualTo("task template!")); //set by SupervisorKickstartContest
            Assert.That(secondRound.TaskText, Is.EqualTo("pseudotask #2!"));
        }


        [Test]
        public async Task ShouldIngestVotes()
        {
            using var compartment = new TestCompartment(TestContext.CurrentContext);

            var syncService = compartment.Container.Resolve<SyncService>();
            var ingestService = compartment.Container.Resolve<MockIngestService>();

            //Setup

            await compartment.ScenarioController.StartUserScenario(
                compartment.GenericScenarios.SupervisorKickstartContest,
                UserCredentials.Supervisor).ScenarioTask;

            Assert.That(await compartment.WaitTillStateMatches(state => state.CurrentTaskMessagelId != null, true),
                Is.True, "Failed kickstarting contest (message id not set)");

            //Run generic users

            Logger.Info("Running 'submit' scenarios");

            var users = new[]
            {
                compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario),
                compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario),
                compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario),
                compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario),
                compartment.ScenarioController.StartUserScenario(compartment.GenericScenarios.ContesterUserScenario),
            };

            await Task.WhenAll(users.Select(u => u.ScenarioTask)); //wait for all user scenarios to complete

            //Ffwd contest

            await compartment.ScenarioController
                .StartUserScenario(
                    async context => { await compartment.GenericScenarios.SupervisorSetDeadlineToNow(context); },
                    UserCredentials.Supervisor).ScenarioTask;

            Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting, true),
                Is.True, "Failed switching to Voting state after deadline hit");

            //Check that there is 1 round and it is in the Voting state

            await ingestService.WaitTillQueueIngested(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

            Assert.That(ingestService.Rounds.Count, Is.EqualTo(1), "Expected exactly 1 rounds ingested");
            var firstRound = ingestService.Rounds.Values.Single();
            Assert.That(firstRound.State, Is.EqualTo(BotContestRoundState.Voting));

            //Fake some votes

            var expectedVotes = new Dictionary<int, int>();

            compartment.AddFakeVoteForEntries(entry =>
            {
                var expectedVote = entry.Id + 1;

                expectedVotes[entry.Id] = expectedVote;

                return expectedVote;
            });

            // Check

            await ingestService.WaitTillQueueIngested(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

            Assert.That(firstRound.State, Is.EqualTo(BotContestRoundState.Voting));


            //Ffwd voting

            await compartment.ScenarioController.StartUserScenario(async context =>
            {
                await compartment.GenericScenarios.SupervisorSetDeadlineToNow(context);

                var votingPinEdited =
                    await context.ReadTillMessageEdited(MockConfiguration.MainChat.Id, TimeSpan.FromSeconds(10));
            }, UserCredentials.Supervisor).ScenarioTask;

            /* ensureTransitionEnded: false here because transition semaphore is locked till inner voting completes */
            Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.ChoosingNextTask,
                    ensureTransitionEnded: false),
                Is.True, "Failed switching to ChoosingNextTask state after deadline hit");

            var winner = compartment.GetCurrentWinnerDirect();

            await compartment.ScenarioController
                .StartUserScenario(context => { context.SendMessage("pseudotask #2!", context.PrivateChat); },
                    UserCredentials.User, useExistingUserId: winner.Id).ScenarioTask;

            Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Contest,
                    ensureTransitionEnded: true), Is.True,
                "Failed switching to ChoosingNextTask state after deadline hit");

            // Check

            await ingestService.WaitTillQueueIngested(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

            Assert.That(ingestService.Rounds.Count, Is.EqualTo(2), "Expected exactly 2 rounds ingested");
            firstRound = ingestService.Rounds[firstRound.Id];

            var firstEntries = ingestService.Tracks.Values.Where(t => t.RoundId == firstRound.Id).ToArray();

            Assert.That(firstEntries.Length, Is.EqualTo(expectedVotes.Count));

            foreach (var (entryId, voteValue) in expectedVotes)
                Assert.That(firstEntries.FirstOrDefault(e => e.Id == entryId)?.Votes, Is.EqualTo(voteValue));

            var secondRound = ingestService.Rounds.Values.Single(r => r.Id != firstRound.Id);
            Assert.That(firstRound.State, Is.EqualTo(BotContestRoundState.Closed));
            Assert.That(secondRound.State, Is.EqualTo(BotContestRoundState.Open));
            Assert.That(firstRound.TaskText, Is.EqualTo("task template!")); //set by SupervisorKickstartContest
            Assert.That(secondRound.TaskText, Is.EqualTo("pseudotask #2!"));
        }
    }
}