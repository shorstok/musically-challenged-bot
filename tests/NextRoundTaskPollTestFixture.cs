using Autofac;
using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Config;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using NodaTime;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;
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

                Assert.That(state.CurrentTaskKind, Is.EqualTo(SelectedTaskKind.Poll),
                    $"Didn't set CurrentTaskKind properly");

                Assert.That(compartment.Repository.GetLastTaskPollWinnerId(), Is.EqualTo(winningSuggestion.AuthorUserId),
                    "LastTaskPollWinner wasn't set correctly");

                Assert.That(compartment.Repository.GetActiveTaskSuggestions(), Is.Empty,
                    "Active suggestions left in post-taskpoll state");
            }
        }

        [Test]
        public async Task ShouldRejectTaskSuggestionsWhenNotInTheCollectionState()
        {
            using (var compartment = new TestCompartment())
            {
                var states = (ContestState[])Enum.GetValues(typeof(ContestState));
                foreach (var state in states.Where(s => s != ContestState.TaskSuggestionCollection))
                {
                    compartment.Repository.UpdateState(s => s.State, state);
                    await compartment.ScenarioController.StartUserScenario(async context =>
                    {
                        context.SendCommand(Schema.TaskSuggestCommandName);
                        var rejectionMessage = await context.ReadTillMessageReceived(context.PrivateChat.Id);

                        Assert.That(rejectionMessage.Text, Contains.Substring(compartment.Localization.TaskSuggestCommandHandler_OnlyAvailableInSuggestionCollectionState),
                            $"Didn't send a rejection message in the state {state}");

                        context.SendMessage("a fake task suggestion message", context.PrivateChat);

                        var helpMessage = await context.ReadTillMessageReceived(context.PrivateChat.Id);

                        Assert.That(helpMessage.Text, 
                            Contains.Substring(LocTokens.SubstituteTokens(compartment.Localization.UnknownCommandUsageTemplate, 
                                Tuple.Create(LocTokens.Details, ""))),
                            "didn't respond with a helper message to an unknown command");
                    }).ScenarioTask;
                }

                var activeSuggestions = compartment.Repository.GetActiveTaskSuggestions();
                Assert.That(activeSuggestions.Count(), Is.EqualTo(0), "Submitted a task suggestion in a non-suggestion-collection state");
            }
        }

        [Test]
        public async Task ShoudlUpdateTaskSuggestionOnRepeatedSuggestions()
        {
            using (var compartment = new TestCompartment())
            {
                // setting up a TaskSuggestionCollection state
                compartment.Repository.UpdateState(s => s.State, ContestState.TaskSuggestionCollection);
                await compartment.Container.Resolve<NextRoundTaskPollController>().StartTaskPollAsync();

                var usrScenario = compartment.ScenarioController.StartUserScenario(
                    async context => context.PersistUserChatId());

                var initialSuggestion = "Initial fake suggestion";
                var updatedSuggestion = "Updated fake suggestion";
                await compartment.ScenarioController.StartUserScenarioForExistingUser(usrScenario.MockUser.Id, async context => 
                {
                    // suggesting the first time
                    context.SendCommand(Schema.TaskSuggestCommandName);
                    
                    var guidelineMessage = await context.ReadTillMessageReceived(context.PrivateChat.Id);
                    Assert.That(guidelineMessage?.Text, Contains.Substring(
                        LocTokens.SubstituteTokens(context.Localization.TaskSuggestCommandHandler_SubmitGuidelines,
                        Tuple.Create(LocTokens.VotingChannelLink, MockConfiguration.Snapshot.VotingChannelInviteLink))),
                        "/tasksuggest command response should contain general submit pretext");

                    context.SendMessage(initialSuggestion, context.PrivateChat);

                    var confirmationMessage = await context.ReadTillMessageReceived(context.PrivateChat.Id);
                    Assert.That(confirmationMessage.Text, Contains.Substring(
                        compartment.Localization.TaskSuggestCommandHandler_SubmitionSucceeded),
                        "Didn't receive a confirmation message for a task sugggestion");

                    // updating a suggestion
                    context.SendCommand(Schema.TaskSuggestCommandName);

                    guidelineMessage = await context.ReadTillMessageReceived(context.PrivateChat.Id);
                    Assert.That(guidelineMessage?.Text, Contains.Substring(
                        LocTokens.SubstituteTokens(context.Localization.TaskSuggestCommandHandler_SubmitGuidelines,
                        Tuple.Create(LocTokens.VotingChannelLink, MockConfiguration.Snapshot.VotingChannelInviteLink))),
                        "/tasksuggest command response should contain general submit pretext");

                    context.SendMessage(updatedSuggestion, context.PrivateChat);

                    confirmationMessage = await context.ReadTillMessageReceived(context.PrivateChat.Id);
                    Assert.That(confirmationMessage.Text, Contains.Substring(
                        compartment.Localization.TaskSuggestCommandHandler_SubmitionSucceeded),
                        "Didn't receive a confirmation message for a task sugggestion");
                }).ScenarioTask;

                var suggestions = compartment.Repository.GetActiveTaskSuggestions()
                    .Where(s => s.AuthorUserId == usrScenario.MockUser.Id);

                Assert.That(suggestions.Count(), Is.EqualTo(1),
                    $"suggestion count from user {usrScenario.MockUser.Id} is {suggestions.Count()}, expected 1");
                Assert.That(suggestions.FirstOrDefault().Description, Contains.Substring(updatedSuggestion),
                    $"The suggestion from user {usrScenario.MockUser.Id} didn't contain his suggestion {updatedSuggestion}");
            }
        }

        [Test]
        public async Task ShouldGoToStandbyIfNoTaskSuggestions()
        {
            using (var compartment = new TestCompartment())
            {
                var clock = compartment.Container.Resolve<IClock>();

                // setting up a TaskSuggestionCollection state
                compartment.Repository.UpdateState(s => s.State, ContestState.TaskSuggestionCollection);
                await compartment.Container.Resolve<NextRoundTaskPollController>().StartTaskPollAsync();

                //No task suggestions sent before deadline hit

                // ffwd
                compartment.Repository.UpdateState(s => s.NextDeadlineUTC, clock.GetCurrentInstant());

                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.Standby),
                    Is.True, "Failed to switch to Standby on not enough contesters");

                var consolidatedEntries = compartment.Repository.CloseNextRoundTaskPollAndConsolidateVotes();
                Assert.That(consolidatedEntries.Count(), Is.EqualTo(0),
                    "Non-consolidated suggestions were left after switching to standby");
                
                Assert.That(compartment.Repository.GetActiveTaskSuggestions(), Is.Empty,
                    "Active suggestions left in post-taskpoll state");
            }
        }
        
        [Test]
        public async Task ShouldFallthroughVotingIfOneTaskSuggestion()
        {
            using (var compartment = new TestCompartment())
            {
                var configuration = compartment.Container.Resolve<IBotConfiguration>();
                var clock = compartment.Container.Resolve<IClock>();
                var mediator = compartment.Container.Resolve<MockMessageMediatorService>();

                // setting up a TaskSuggestionCollection state
                compartment.Repository.UpdateState(s => s.State, ContestState.TaskSuggestionCollection);
                await compartment.Container.Resolve<NextRoundTaskPollController>().StartTaskPollAsync();

                //Send single task suggestion

                await compartment.ScenarioController
                    .StartUserScenario(compartment.GenericScenarios.TaskSuggesterUserScenario)
                    .ScenarioTask;

                var singleSuggestion = compartment.Repository.GetActiveTaskSuggestions().Single();

                // ffwd
                compartment.Repository.UpdateState(s => s.NextDeadlineUTC, clock.GetCurrentInstant());

                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.Contest),
                    Is.True, "Failed to fallthrough to Contest state");

                //Assert

                var consolidatedEntries = compartment.Repository.CloseNextRoundTaskPollAndConsolidateVotes();
                Assert.That(consolidatedEntries.Count(), Is.EqualTo(0),
                    "Non-consolidated suggestions were left after falling through");

                var state = compartment.Repository.GetOrCreateCurrentState();
                Assert.That(state.CurrentTaskTemplate, Is.EqualTo(singleSuggestion.Description),
                    $"Started contest with a wrong task: {state.CurrentTaskTemplate} when should be {singleSuggestion.Description}");

                Assert.That(state.CurrentTaskKind, Is.EqualTo(SelectedTaskKind.Poll),
                    $"Didn't set CurrentTaskKind properly");

                Assert.That(compartment.Repository.GetLastTaskPollWinnerId(), Is.EqualTo(singleSuggestion.AuthorUserId),
                    "LastTaskPollWinner wasn't set correctly");

                Assert.That(compartment.Repository.GetActiveTaskSuggestions(), Is.Empty,
                    "Active suggestions left in post-taskpoll state");
            }

        }

        [Test]
        public async Task ShouldGoToStandbyIfNotEnoughVotes()
        {
            using (var compartment = new TestCompartment())
            {
                var configuration = compartment.Container.Resolve<IBotConfiguration>();

                // setting up a contest
                compartment.Repository.UpdateState(s => s.State, ContestState.TaskSuggestionCollection);
                await compartment.Container.Resolve<NextRoundTaskPollController>().StartTaskPollAsync();

                await compartment.GenericScenarios.PopulateWithTaskSuggestionsAndSwitchToVoting(compartment,
                    configuration.MinAllowedContestEntriesToStartVoting);

                await compartment.GenericScenarios.FinishNextRoundTaskPollAndSimulateVoting(compartment,
                    Math.Max(0, configuration.MinAllowedContestEntriesToStartVoting - 1));

                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.Standby),
                    Is.True, "Failed to switch to Standby on not enough votes");

                var consolidatedEntries = compartment.Repository.CloseNextRoundTaskPollAndConsolidateVotes();
                Assert.That(consolidatedEntries.Count(), Is.EqualTo(0),
                    "Non-consolidated suggestions were left after switching to standby");
            }
        }

        [Test]
        public async Task KickstartedPollShouldSucceed()
        {
            using (var compartment = new TestCompartment())
            {
                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    context.SendCommand(Schema.KickstartNextRoundTaskPollCommandName);

                    var pleaseConfirmResponse = await context.ReadTillMessageReceived(context.PrivateChat.Id);
                    Assert.That(pleaseConfirmResponse, Is.Not.Null,
                        "Command response was not sent");
                    Assert.That(pleaseConfirmResponse.ReplyMarkup.InlineKeyboard?.FirstOrDefault()?.Count(),
                        Is.EqualTo(2), "Reply markup doesn't have 2 buttons");

                    context.SendQuery("y", pleaseConfirmResponse);

                    var confirmationResponse = await context.ReadTillMessageReceived(context.PrivateChat.Id);
                    Assert.That(confirmationResponse, Is.Not.Null,
                        "Confirmation message was not sent");
                }, UserCredentials.Supervisor).ScenarioTask;

                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.TaskSuggestionCollection),
                    Is.True, "Failed to switch to TaskSuggestionCollection on kickstart issued");

                await compartment.GenericScenarios.PopulateWithTaskSuggestionsAndSwitchToVoting(compartment, 2);

                // voting
                var winningSuggestion = await compartment.GenericScenarios.FinishNextRoundTaskPollAndSimulateVoting(compartment);
                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.Contest),
                    Is.True, "Failed to switch to Contest");

                var state = compartment.Repository.GetOrCreateCurrentState();
                Assert.That(state.CurrentTaskTemplate, Is.EqualTo(winningSuggestion.Description),
                    $"Started contest with a wrong task: {state.CurrentTaskTemplate} when should be {winningSuggestion.Description}");

                Assert.That(state.CurrentTaskKind, Is.EqualTo(SelectedTaskKind.Poll),
                    $"Didn't set CurrentTaskKind properly");

                Assert.That(compartment.Repository.GetLastTaskPollWinnerId(), Is.EqualTo(winningSuggestion.AuthorUserId),
                    "LastTaskPollWinner wasn't set correctly");

                Assert.That(compartment.Repository.GetActiveTaskSuggestions(), Is.Empty,
                    "Active suggestions left in post-taskpoll state");
            }
        }
    }
}
