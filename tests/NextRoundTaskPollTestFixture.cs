using Autofac;
using log4net;
using musicallychallenged.Domain;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using NodaTime;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tests.DI;
using tests.Mockups;
using tests.Mockups.Messaging;

namespace tests
{
    [TestFixture]
    class NextRoundTaskPollTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(NextRoundTaskPollTestFixture));

        [Test]
        public async Task ShouldSwitchToNextRoundTakPollStateWhenWinnerInitiatesPoll()
        {
            using (var compartment = new TestCompartment())
            {
                // Setting up
                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToArray();

                var nrtpQuery = NewTaskSelectorController.NextRoundTaskPollCallbackId;
                await compartment.GenericScenarios.
                    FinishContestAndSimulateVoting(compartment, (winnerCntx, msg) => winnerCntx.SendQuery(nrtpQuery, msg));

                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.TaskSuggestionCollection),
                    Is.True, "Didn't switch to TaskSuggestionCollection");
            }
        }

        [Test]
        public async Task ShouldStartTheNextRoundWithMaxVotedTaskSuggestion()
        {
            using (var compartment = new TestCompartment())
            {
                // setting up a poll
                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToArray();

                var nrtpQuery = NewTaskSelectorController.NextRoundTaskPollCallbackId;
                await compartment.GenericScenarios.
                    FinishContestAndSimulateVoting(compartment, (winnerCntx, msg) => winnerCntx.SendQuery(nrtpQuery, msg));

                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.TaskSuggestionCollection),
                    Is.True, "Didn't switch to TaskSuggestionCollection");

                // creating suggestions
                await compartment.GenericScenarios.PopulateWithTaskSuggestionsAndSwitchToVoting(compartment, 2);

                // voting
                var winningSuggestion = await compartment.GenericScenarios.FinishNextRoundTaskPollAndSimulateVoting(compartment);
                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.Contest),
                    Is.True, "Failed to switch to Contest");

                var state = compartment.Repository.GetOrCreateCurrentState();
                Assert.That(state.CurrentTaskTemplate, Is.EqualTo(winningSuggestion.Description),
                    $"Started contest with a wrong task: {state.CurrentTaskTemplate} when should be {winningSuggestion.Description}");
            }
        }
    }
}
