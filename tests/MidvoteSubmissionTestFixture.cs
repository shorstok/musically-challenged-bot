﻿using Autofac;
using log4net;
using musicallychallenged.Commands;
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
    class MidvoteSubmissionTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(MidvoteSubmissionTestFixture));

        [Test]
        public async Task ShouldSubmitEntrySuccessfully()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                var midvoteController = compartment.Container.Resolve<MidvoteEntryController>();
                var messageMediator = compartment.Container.Resolve<MockMessageMediatorService>();
                var votingController = compartment.Container.Resolve<VotingController>();

                // setting up a poll
                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToArray();

                Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting, false),
                       Is.True, "Failed switching to Voting state after deadline hit");

                // Creating a pin
                var pin = "1234";
                await compartment.GenericScenarios.SupervisorAddPin(compartment, pin);

                var pinCount = await midvoteController.GetCurrentPinCount();
                Assert.That(pinCount, Is.EqualTo(1),
                    $"Current pinCount is {pinCount}, expected 1");

                // voting for some the first entry
                var voterCount = 5;
                var targetVotingMessage = votingEntities[0].Item2;
                for (int i = 0; i < voterCount; i++)
                {
                    await compartment.ScenarioController.StartUserScenario(context => 
                    {
                        var maxVoteValue = votingController.VotingSmiles.Max(x => x.Key);
                        var maxVoteSmile = votingController.VotingSmiles[maxVoteValue];
                        var button = targetVotingMessage.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                        FirstOrDefault(b => b.Text == maxVoteSmile);

                        context.SendQuery(button.CallbackData, targetVotingMessage);
                        
                        return Task.CompletedTask;
                    }).ScenarioTask;
                }

                // Submitting an entry
                await compartment.ScenarioController.StartUserScenario(async context => 
                    await compartment.GenericScenarios.ContesterUserScenario(context, pin, 
                    postSubmissionValidation: () =>
                    {
                        // entry exist
                        var entry = compartment.Repository.GetActiveContestEntryForUser(context.MockUser.Id);
                        Assert.That(entry, Is.Not.Null, "Didn't create a contest entry");

                        // voting controls
                        var votingMessage = messageMediator.GetMockMessage(entry.ContainerChatId, entry.ContainerMesssageId);
                        Assert.That(votingMessage.ReplyMarkup?.InlineKeyboard?.SelectMany(buttons => buttons)?.Count(),
                            Is.EqualTo(5),
                            $"Didnt create five voting buttons for a midvoting entry");

                        // votes amount
                        var entryVotes = compartment.Repository.GetVotesForEntry(entry.Id);
                        Assert.That(entryVotes.Count(), Is.EqualTo(voterCount),
                            $"Expected {voterCount} votes for a midvote entry, but got {entryVotes.Count()}");
                        
                        return Task.CompletedTask;
                    }))
                    .ScenarioTask;

                pinCount = await midvoteController.GetCurrentPinCount();
                Assert.That(pinCount, Is.EqualTo(0),
                    $"Current pinCount is {pinCount}, expected 0");
            }
        }

        [Test]
        public async Task UnusedPinShouldBeRemoved()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                var midvoteController = compartment.Container.Resolve<MidvoteEntryController>();

                // setting up a poll
                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToArray();

                Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting, false),
                       Is.True, "Failed switching to Voting state after deadline hit");

                // Creating a pin
                var pin = "1234";
                await compartment.GenericScenarios.SupervisorAddPin(compartment, pin);

                var pinCount = await midvoteController.GetCurrentPinCount();
                Assert.That(pinCount, Is.EqualTo(1),
                    $"Current pinCount is {pinCount}, expected 1");

                await compartment.GenericScenarios.FinishContestAndSimulateVoting(compartment);

                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.Contest, false), Is.True,
                    "Failed to switch to Contest state");

                pinCount = await midvoteController.GetCurrentPinCount();
                Assert.That(pinCount, Is.EqualTo(0),
                    $"Current pinCount is {pinCount}, expected 0");
            }
        }

        [Test]
        public async Task ShouldDenyAddingPinWhenNotInVoting()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                var states = (ContestState[])Enum.GetValues(typeof(ContestState));
                foreach (var state in states.Where(s => s != ContestState.Voting))
                {
                    compartment.Repository.UpdateState(s => s.State, state);
                    await compartment.ScenarioController.StartUserScenario(async context =>
                    {
                        context.SendCommand(Schema.AddMidvotePin);

                        var message = await context.ReadTillMessageReceived(context.PrivateChat.Id);
                        Assert.That(message, Is.Not.Null, "'Wrong state' message was not received");
                        Assert.That(message.Text, Contains.Substring("denied"),
                            "Message didn't contain an appropriate 'denied' string");
                    }, UserCredentials.Supervisor).ScenarioTask;
                }
            }
        }

        [Test]
        public async Task ShouldDenyMidvoteSubmissionIfSubmittedEntryExists()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                // setting up a poll
                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToArray();

                Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting, false),
                       Is.True, "Failed switching to Voting state after deadline hit");

                // Creating a pin
                var pin = "1234";
                await compartment.GenericScenarios.SupervisorAddPin(compartment, pin);

                var existingUserId = compartment.Repository.GetActiveContestEntries()
                    .Where(entry => entry.ContainerMesssageId == votingEntities[0].Item2.MessageId)
                    .FirstOrDefault().AuthorUserId;

                await compartment.ScenarioController.StartUserScenarioForExistingUser(existingUserId, async context =>
                {
                    context.SendCommand(Schema.SubmitCommandName);

                    var message = await context.ReadTillMessageReceived(context.PrivateChat.Id);

                    Assert.That(message, Is.Not.Null, "Didn't receive 'submission denied' message");
                    Assert.That(message.Text, 
                        Contains.Substring(compartment.Localization.SubmitContestEntryCommandHandler_OnlyAvailableInContestState),
                        "Didn't send an appropriate message when rejecting an entry submission in a non-contest state");
                }).ScenarioTask;
            }
        }

        [Test]
        public async Task ShoudDenyInvalidPin()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                // setting up a poll
                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToArray();

                Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Voting, false),
                       Is.True, "Failed switching to Voting state after deadline hit");

                // Creating a pin
                var pin = "1234";
                await compartment.GenericScenarios.SupervisorAddPin(compartment, pin);

                await compartment.ScenarioController.StartUserScenario(async context => 
                    await compartment.GenericScenarios.ContesterUserScenario(context, "invalid pin", assertPinIsInvalid: false))
                    .ScenarioTask;
            }
        }

        [Test]
        public async Task ShouldRemoveVotingControlsAndUpdateStatsMessageOnVotingFinalization()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                var midvoteController = compartment.Container.Resolve<MidvoteEntryController>();
                var messageMediator = compartment.Container.Resolve<MockMessageMediatorService>();
                var votingController = compartment.Container.Resolve<VotingController>();
                var clock = compartment.Container.Resolve<IClock>();

                // setting up a poll
                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToList();

                Assert.That(await compartment.WaitTillStateMatches(stateVar => stateVar.State == ContestState.Voting, false),
                       Is.True, "Failed switching to Voting state after deadline hit");

                // Creating a pin
                var pin = "1234";
                await compartment.GenericScenarios.SupervisorAddPin(compartment, pin);

                var pinCount = await midvoteController.GetCurrentPinCount();
                Assert.That(pinCount, Is.EqualTo(1),
                    $"Current pinCount is {pinCount}, expected 1");

                // voting for some the first entry
                var voterCount = 5;
                var targetVotingMessage = votingEntities[0].Item2;
                for (int i = 0; i < voterCount; i++)
                {
                    await compartment.ScenarioController.StartUserScenario(context =>
                    {
                        var maxVoteValue = votingController.VotingSmiles.Max(x => x.Key);
                        var maxVoteSmile = votingController.VotingSmiles[maxVoteValue];
                        var button = targetVotingMessage.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                        FirstOrDefault(b => b.Text == maxVoteSmile);

                        context.SendQuery(button.CallbackData, targetVotingMessage);
                    }).ScenarioTask;
                }

                ActiveContestEntry midvoteEntry = null;

                // Submitting an entry
                await compartment.ScenarioController.StartUserScenario(async context =>
                    await compartment.GenericScenarios.ContesterUserScenario(context, pin,
                    postSubmissionValidation: () =>
                    {
                        // entry exist
                        midvoteEntry = compartment.Repository.GetActiveContestEntryForUser(context.MockUser.Id);
                        Assert.That(midvoteEntry, Is.Not.Null, "Didn't create a contest entry");

                        // voting controls
                        var votingMessage = messageMediator.GetMockMessage(midvoteEntry.ContainerChatId, midvoteEntry.ContainerMesssageId);
                        Assert.That(votingMessage.ReplyMarkup?.InlineKeyboard?.SelectMany(buttons => buttons)?.Count(),
                            Is.EqualTo(5),
                            $"Didnt create five voting buttons for a midvoting entry");

                        // votes amount
                        var entryVotes = compartment.Repository.GetVotesForEntry(midvoteEntry.Id);
                        Assert.That(entryVotes.Count(), Is.EqualTo(voterCount),
                            $"Expected {voterCount} votes for a midvote entry, but got {entryVotes.Count()}");

                        return Task.CompletedTask;
                    }))
                    .ScenarioTask;

                pinCount = await midvoteController.GetCurrentPinCount();
                Assert.That(pinCount, Is.EqualTo(0),
                    $"Current pinCount is {pinCount}, expected 0");

                var state = compartment.Repository.GetOrCreateCurrentState();
                Assert.That(state.CurrentVotingStatsMessageId, Is.Not.Null,
                    "state.CurrentVotingStatsMessageId is null before finalization");

                //await compartment.GenericScenarios.FinishContestAndSimulateVoting(compartment);
                compartment.Repository.UpdateState(s => s.NextDeadlineUTC, clock.GetCurrentInstant());
                Assert.That(await compartment.WaitTillStateMatches(s => s.State == ContestState.ChoosingNextTask, false), Is.True,
                    "Didn't switch to ChoosingNextTask after voting");

                var votingStatsMsg = messageMediator.GetMockMessage((long)state.VotingChannelId, (int)state.CurrentVotingStatsMessageId);
                Assert.That(votingStatsMsg.Text, Does.Not.Contain("Votes hidden"),
                    "Voting stats message wasn't updated after voting finalization");

                var midvoteMsg = messageMediator.GetMockMessage(midvoteEntry.ContainerChatId, midvoteEntry.ContainerMesssageId);

                var votingMessages = votingEntities.Select(e => e.Item2)
                    .Select(m => messageMediator.GetMockMessage(m.Chat.Id, m.MessageId))
                    .ToList();
                votingMessages.Add(midvoteMsg);

                foreach (var msg in votingMessages)
                {
                    Assert.That(msg.ReplyMarkup?.InlineKeyboard?.SelectMany(buttons => buttons)?.Count(),
                        Is.EqualTo(0), $"Midvote message markup was not cleared for a message {msg.MessageId}");
                }
            }
        }
    }
}
