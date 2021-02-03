using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged.Domain;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using NUnit.Framework;
using Telegram.Bot.Types.ReplyMarkups;
using tests.DI;
using tests.Mockups;
using tests.Mockups.Messaging;

namespace tests
{
    [TestFixture]
    public class InnerCiricleVotingTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(InnerCiricleVotingTestFixture));

        [Test]
        public async Task InnerCircleApprovalScenarioShouldSucceed()
        {
            const string mockTaskText = "Lorem ipsum";

            using (var compartment = new TestCompartment())
            {
                //Setup

                var mediator = compartment.Container.Resolve<MockMessageMediatorService>();
                var votingController = compartment.Container.Resolve<VotingController>();

                var votingEntities = compartment.GenericScenarios.
                    PrepareVotingCycle(MockConfiguration.Snapshot.MinAllowedContestEntriesToStartVoting + 1).
                    ToArray();

                //Create a bunch of admins

                var admins = Enumerable.Range(0, 3).Select(i =>
                {
                    return compartment.ScenarioController.StartUserScenario(
                        async context => context.PersistUserChatId(),
                        UserCredentials.Admin);
                }).ToArray();

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
                            $"Didnt create five voting buttons for entry {votingEntity.Item2.Text}");
                    }
                }).ScenarioTask;

                //Vote for entry 1 with 5 users with max vote
                
                var voterCount = 5;

                for (var nuser = 0; nuser < voterCount; nuser++)
                    await compartment.ScenarioController.StartUserScenario(async context =>
                    {
                        var maxVoteValue = VotingController._votingSmiles.Max(x => x.Key);
                        var maxVoteSmile = VotingController._votingSmiles[maxVoteValue];
                        var button = votingEntities[1].Item2.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                            FirstOrDefault(b => b.Text == maxVoteSmile);

                        Assert.That(button, Is.Not.Null,
                            $"Max voting value button (labelled {maxVoteSmile}) not found in reply markup");

                        context.SendQuery(button.CallbackData, votingEntities[1].Item2);
                    }).ScenarioTask;

                //Ffwd voting

                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    await compartment.GenericScenarios.SupervisorSetDeadlineToNow(context);

                    var votingPinEdited =
                        await context.ReadTillMessageEdited(MockConfiguration.MainChat.Id, TimeSpan.FromSeconds(10));
                }, UserCredentials.Supervisor).ScenarioTask;

                //Continue in winning user scenario

                var taskSubmittedSource = new TaskCompletionSource<bool>();

                var adminTasks = new List<Task<object>>();

                for (var i = 0; i < admins.Length; i++)
                {
                    var admin = admins[i];

                    

                    var task = compartment.ScenarioController.StartUserScenarioForExistingUser(
                        admin.MockUser.Id,
                        async context =>
                        {
                            //Wait for winner to sumbit task
                            await taskSubmittedSource.Task;

                            //Wait for message with voting controls
                            var ctrlMsg = await context.ReadTillPrivateMessageReceived(
                                msg => msg.Text.IndexOf(mockTaskText, StringComparison.Ordinal) != -1,
                                TimeSpan.FromSeconds(1));

                            Assert.That(ctrlMsg?.ReplyMarkup?.InlineKeyboard?.
                                FirstOrDefault()?.
                                OfType<InlineKeyboardButton>().Count(), Is.EqualTo(2));

                            //Approve new task

                            var yesButton = ctrlMsg.ReplyMarkup?.InlineKeyboard?.FirstOrDefault()?.
                                FirstOrDefault(b => b.Text == context.Localization.AdminApproveLabel);

                            Assert.That(yesButton, Is.Not.Null, "'Approve' button not found in query answer");

                            context.SendQuery(yesButton.CallbackData, ctrlMsg);

                            //Wait till global verdict
                            Assert.That(await compartment.WaitTillStateMatches(state =>
                                    state.State == ContestState.Contest),
                                Is.True, "Failed switching to Contest");

                            //Check that admin dialog gets recycled 

                            context.ClearMessages();

                            context.SendCommand("hello hello");

                            var reply = await context.ReadTillPrivateMessageReceived(t => true,
                                TimeSpan.FromSeconds(1));

                            Logger.Info($"got {reply.Text}");
                        }, UserCredentials.Admin).ScenarioTask;

                    adminTasks.Add(task);
                }

                //Submit task for new round

                await compartment.ScenarioController.StartUserScenarioForExistingUser(
                    votingEntities[1].Item1.From.Id,
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

                        Assert.That(messageWithControls.ReplyMarkup.InlineKeyboard.FirstOrDefault().Count(), Is.EqualTo(2),
                            "Winner task selector should have 2 reply buttons (for random task selection and starting the next round task poll)");

                        winnerCtx.SendMessage(mockTaskText, winnerCtx.PrivateChat);

                        //Unblock admin premoderation scenarios
                        taskSubmittedSource.SetResult(true);
                    }).ScenarioTask;

                Logger.Info("Waiting for all admins to approve submitted task");
                await Task.WhenAll(adminTasks);

                Assert.That(
                    compartment.Repository.GetOrCreateCurrentState().CurrentTaskTemplate,
                    Contains.Substring(mockTaskText),
                    "Invalid task chosen for next round");

            }
        }
    }
}