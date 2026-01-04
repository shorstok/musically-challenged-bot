using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Dapper;
using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Config;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using NodaTime;
using NUnit.Framework;
using Telegram.Bot.Types;
using tests.DI;
using tests.Mockups;
using tests.Mockups.Messaging;
using User = musicallychallenged.Domain.User;

namespace tests
{
    [TestFixture]
    public class PostponeTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(PostponeTestFixture));

        [Test]
        public async Task ShouldDenyRequestsForNewUsers()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                compartment.Repository.UpdateState(state => state.State, ContestState.Contest);

                await compartment.ScenarioController.StartUserScenario(async context =>
                {
                    context.SendCommand(Schema.PostponeCommandName);

                    var answer = await context.ReadTillMessageReceived(context.PrivateChat.Id);

                    Assert.That(answer?.Text, Contains.Substring(compartment.Localization.PostponeCommandHandler_OnlyForKnownUsers));

                    Logger.Info($"Got {answer?.Text}");
                }).ScenarioTask;
            }
        }   
        
        [Test]
        public async Task ShouldDiscardOpenRequestsAtNextRound()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                var stateController = compartment.Container.Resolve<StateController>();
                var prepResult = await PrepareCompartmentWithUserHistory(compartment);
                

                var config = prepResult.Item1;
                var users = prepResult.Item3;

                //Aim for shortest option, but one that's longer than 1 day
                //because last-minute option buttons would available only in contest last day

                var targetOption = config
                    .PostponeOptions
                    .OrderBy(o => o.AsDuration)
                    .First(op=>op.AsDuration >= Duration.FromDays(1));

                //Post requests but don't reach PostponeQuorum for requests to linger unitl next round

                for (int i = 0; i < config.PostponeQuorum-1; i++)
                {
                    await compartment.ScenarioController.StartUserScenarioForExistingUser(
                        users[i].MockUser.Id,
                        async context =>
                        {
                            //Sumbit something

                            await compartment.GenericScenarios.ContesterUserScenario(context);

                            //Run postpone command and pick button with shortest duration

                            var result =
                                await compartment.GenericScenarios.PostponeUserScenario(context,
                                    buttons =>
                                    {
                                        return buttons.FirstOrDefault(b =>
                                            b.Text == targetOption.GetLocalizedName(compartment.Localization));
                                    });

                            context.PersistUserChatId();

                            Logger.Info($"User {context.MockUser.Username}: OK, got {result}");
                        }).ScenarioTask;
                }

                //Finish round

                var winnerId = await compartment.GenericScenarios.FinishContestAndSimulateVoting(compartment);

                Assert.That(winnerId, Is.Not.Null);

                Assert.That(await compartment.WaitTillStateMatches(state => state.State == ContestState.Contest, false),
                    Is.True, "Failed switching to Contest state");

                await stateController.WaitForStateTransition();

                //Output all request states

                Logger.Info($"FINAL POSTPONE REQUEST STATES");

                using (var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository))
                {
                    var allRequests = connection.Query<PostponeRequest>(@"select * from PostponeRequest").
                        ToArray();

                    foreach (var request in allRequests)
                    {
                        Logger.Info($"> Request id {request.Id} from user#{request.UserId}/round{request.ChallengeRoundNumber}" +
                                    $" for {request.AmountMinutes}minutes final state: {request.State}");
                         
                        Assert.That(request.State,Is.EqualTo(PostponeRequestState.ClosedDiscarded));
                    }
                }
            }
        }
    
        [Test]
        public async Task ShouldLimitPostponeAmountForOneRound()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                var prepResult = await PrepareCompartmentWithUserHistory(compartment);

                var config = prepResult.Item1;
                var initialDeadline = prepResult.Item2;
                var users = prepResult.Item3;

                var targetOption = config.PostponeOptions.OrderByDescending(o => o.AsDuration).First();
                var lastMomentPostponeOption =
                    config.PostponeOptions.FirstOrDefault(o => o.AsDuration < Duration.FromHours(1));

                var previousDeadline = initialDeadline;

                Duration quotaLeft = Duration.Zero;
                do
                {
                    quotaLeft = Duration.FromHours(config.PostponeHoursAllowed) -
                                    Duration.FromMinutes(compartment.Repository
                                        .GetUsedPostponeQuotaForCurrentRoundMinutes());

                    bool shouldPostponeSucceed = quotaLeft >= targetOption.AsDuration;

                    Logger.Info($"Going to postpone, estimated quota left: {quotaLeft}, estimated postpone should {(shouldPostponeSucceed?"succeed":"fail")}");

                    for (int i = 0; i < config.PostponeQuorum; i++)
                    {
                        await compartment.ScenarioController.StartUserScenarioForExistingUser(
                            users[i].MockUser.Id,
                            async context =>
                            {
                                //Pick button with longest duration

                                var result =
                                    await compartment.GenericScenarios.PostponeUserScenario(context,
                                        buttons =>
                                        {
                                            return buttons.FirstOrDefault(b =>
                                                b.Text == targetOption.GetLocalizedName(compartment.Localization));
                                        });

                                context.PersistUserChatId();

                                Logger.Info($"User {context.MockUser.Username}: OK, got {result}");
                            }).ScenarioTask;
                    }

                    var expectedDeadline = previousDeadline;
                    
                    if(shouldPostponeSucceed)
                        expectedDeadline+= targetOption.AsDuration;

                    Assert.That(compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC,
                        Is.Not.EqualTo(initialDeadline));
                    Assert.That(compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC,
                        Is.EqualTo(expectedDeadline));

                    Logger.Info(
                        $"Deadline now is {compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC}, " +
                        $"advance {targetOption.AsDuration} / {targetOption.GetLocalizedName(compartment.Localization)}");

                    previousDeadline = compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC;

                } while (quotaLeft > targetOption.AsDuration);

                Logger.Info($"Final postpone requests");

                using (var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository))
                {
                    var allRequests = connection.Query<PostponeRequest>(@"select * from PostponeRequest").
                        ToArray();

                    foreach (var request in allRequests)
                    {
                        Logger.Info($"Request id {request.Id} from user#{request.UserId}/round{request.ChallengeRoundNumber}" +
                                    $" for {request.AmountMinutes}minutes final state: {request.State}");
                         
                        Assert.That(request.State,Is.Not.EqualTo(PostponeRequestState.Open));
                    }
                }
            }
        }

        private static async Task<Tuple<IBotConfiguration, Instant, UserScenarioContext[]>> PrepareCompartmentWithUserHistory(TestCompartment compartment)
        {
            //Setup
            var config = compartment.Container.Resolve<IBotConfiguration>();

            compartment.Repository.UpdateState(state => state.CurrentChallengeRoundNumber, 102);

            await compartment.ScenarioController.StartUserScenario(
                compartment.GenericScenarios.SupervisorKickstartContest,
                UserCredentials.Supervisor).ScenarioTask;

            Assert.That(await compartment.WaitTillStateMatches(state => state.CurrentTaskMessagelId != null, false),
                Is.True,
                "Failed kickstarting contest (message id not set)");

            var initialDeadline = compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC;

            //Run generic users

            Logger.Info("Running 'submit' scenarios");

            var users = Enumerable.Range(0, 10).Select(t => compartment.ScenarioController.StartUserScenario(async context =>
            {
                await compartment.GenericScenarios.ContesterUserScenario(context);

                //Create fake entry for previous round

                var user = compartment.Repository.CreateOrGetUserByTgIdentity(context.MockUser);
                var state = compartment.Repository.GetOrCreateCurrentState();

                context.PersistUserChatId();

                compartment.Repository.GetOrCreateContestEntry(user,
                    1, 1, 1,
                    state.CurrentChallengeRoundNumber - 1,
                    out _);

                var entry = compartment.Repository.GetActiveContestEntryForUser(user.Id);
                entry.ConsolidatedVoteCount = 10; //make entry finished
                compartment.Repository.UpdateContestEntry(entry);
            })).ToArray();

            await Task.WhenAll(users.Select(u => u.ScenarioTask)); //wait for all user scenarios to complete

            Assert.That(compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC, Is.EqualTo(initialDeadline));

            return Tuple.Create(config, initialDeadline, users);
        }


        [Test]
        public async Task ShouldDiscardAllOpenRequests()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                for (int i = 0; i < 10; i++)
                {
                    var fakeUser = compartment.Repository.CreateOrGetUserByTgIdentity(new Telegram.Bot.Types.User
                    {
                        Id = i + 1,
                        FirstName = $"fake-user-{i + 1}",
                    });

                    compartment.Repository.CreatePostponeRequestRetrunOpen(fakeUser,Duration.FromHours(1));
                }

                var postponeService = compartment.Container.Resolve<PostponeService>();

                await postponeService.CloseRefundAllPostponeRequests(PostponeRequestState.ClosedDiscarded);

                //Ensure requests were assigned correct final states

                using (var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository))
                {
                    var allRequests = connection.Query<PostponeRequest>(@"select * from PostponeRequest").
                        ToArray();

                    foreach (var request in allRequests)
                    {
                        Console.WriteLine($"Request id {request.Id} from user#{request.UserId} for {request.AmountMinutes}minutes final state: {request.State}");
                         
                        Assert.That(request.State, Is.EqualTo(PostponeRequestState.ClosedDiscarded));
                    }
                }


            }
        }        
        
        [Test]
        public async Task ShouldPickLargestPostponeRequest()
        {
            using (var compartment = new TestCompartment(TestContext.CurrentContext))
            {
                var postponeCompletedSource = new TaskCompletionSource<bool>();
                compartment.ScenarioController.StartUserScenario(async context =>
                {
                    await postponeCompletedSource.Task;
                    
                    var token = new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;
                    var reason = compartment.Localization.PostponeService_DeadlinePostponedQuorumFulfilled;
                    
                    Message announcement = null;
                    try
                    {
                        do
                        {
                            announcement = await context.ReadTillMessageReceived(MockConfiguration.MainChat.Id);
                        } while (!(announcement.Text.Contains(reason) || token.IsCancellationRequested));
                    }
                    catch (TimeoutException)
                    {
                        Assert.Fail("Couldn't read any more messages");
                    }

                    Assert.True(!token.IsCancellationRequested,
                        "Couldn't receive a deadline change announcement message");
                });
                
                var initialDeadline = Instant.FromUtc(2020, 01, 01, 11, 00);
                var currentRoundNumber = 123;

                compartment.Repository.UpdateState(state => state.State, ContestState.Contest);
                compartment.Repository.UpdateState(state => state.CurrentChallengeRoundNumber, currentRoundNumber);
                compartment.Repository.UpdateState(state => state.NextDeadlineUTC, initialDeadline);

                var users = new List<User>();

                for (int i = 0; i < 10; i++)
                {
                    var fakeUser = compartment.Repository.CreateOrGetUserByTgIdentity(new Telegram.Bot.Types.User
                    {
                        Id = i + 1,
                        FirstName = $"fake-user-{i + 1}",
                    });

                    //Create fake entry for previous round
                    compartment.Repository.GetOrCreateContestEntry(fakeUser,1,1,1,currentRoundNumber-1,out _);

                    users.Add(fakeUser);
                }

                var seed = users
                    .Take(3)
                    .Select((u, i) => 
                        Tuple.Create(
                            u, 
                            Duration.FromDays((i + 2) % 3 + 1)))
                    .ToArray();

                var postponeService = compartment.Container.Resolve<PostponeService>();

                var finalDeadline = initialDeadline + seed.Max(d => d.Item2);

                for (int i = 0; i < seed.Length; i++)
                {
                    var reply = await postponeService.DemandPostponeRequest(seed[i].Item1, seed[i].Item2);

                    Assert.That(reply,
                        Is.EqualTo(i != seed.Length - 1
                            ? PostponeService.PostponeResult.Accepted
                            : PostponeService.PostponeResult.AcceptedAndPostponed));

                    Assert.That(compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC,
                        Is.EqualTo(i!=seed.Length-1 
                            ? initialDeadline
                            : finalDeadline));
                }

                //Ensure requests were assigned correct final states

                var requestToBePicked = seed.OrderByDescending(s => s.Item2).First();

                using (var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository))
                {
                    var allRequests = connection.Query<PostponeRequest>(@"select * from PostponeRequest").
                        ToArray();

                    foreach (var request in allRequests)
                    {
                        Console.WriteLine($"Request id {request.Id} from user#{request.UserId} for {request.AmountMinutes}minutes final state: {request.State}");
                         
                        Assert.That(request.State,
                            request.AmountMinutes == (long) requestToBePicked.Item2.TotalMinutes
                                ? Is.EqualTo(PostponeRequestState.ClosedSatisfied)
                                : Is.EqualTo(PostponeRequestState.ClosedDiscarded));
                    }
                }
            }
        }

        [Test]
        public async Task ShouldRefundCoinsWhenRequestsDiscarded()
        {
            using var compartment = new TestCompartment(TestContext.CurrentContext);
            
            compartment.Repository.UpdateState(state => state.State, ContestState.Contest);
            compartment.Repository.UpdateState(state => state.CurrentChallengeRoundNumber, 100);

            var users = new List<User>();
            var initialBalances = new Dictionary<long, long>();

            // Create users with initial balances (less than quorum to avoid auto-postpone)
            for (int i = 0; i < 2; i++)
            {
                var fakeUser = compartment.Repository.CreateOrGetUserByTgIdentity(new Telegram.Bot.Types.User
                {
                    Id = i + 100,
                    FirstName = $"test-user-{i}",
                });

                // Give users initial balance
                compartment.Repository.AddPesnocentsToUser(fakeUser.Id, 700); // 7.00 pesnocoins

                fakeUser = compartment.Repository.GetExistingUserWithTgId(fakeUser.Id);
                users.Add(fakeUser);
                initialBalances[fakeUser.Id] = fakeUser.Pesnocent;

                // Create fake entry for previous round (required to use postpone)
                compartment.Repository.GetOrCreateContestEntry(fakeUser, 1, 1, 1, 99, out _);
            }

            var postponeService = compartment.Container.Resolve<PostponeService>();

            // Create postpone requests (costs 100 pesnocents each)
            foreach (var user in users)
            {
                var result = await postponeService.DemandPostponeRequest(user, Duration.FromHours(1));
                Assert.That(result, Is.EqualTo(PostponeService.PostponeResult.Accepted));
            }

            // Verify balance was deducted
            foreach (var user in users)
            {
                var updatedUser = compartment.Repository.GetExistingUserWithTgId(user.Id);
                Assert.That(updatedUser.Pesnocent, Is.EqualTo(600),
                    $"User {user.Id} should have 600 pesnocents after deduction (700 - 100)");
            }

            // Close and refund all requests (simulating new round start)
            await postponeService.CloseRefundAllPostponeRequests(PostponeRequestState.ClosedDiscarded);

            // Verify balance was refunded
            foreach (var user in users)
            {
                var refundedUser = compartment.Repository.GetExistingUserWithTgId(user.Id);
                Assert.That(refundedUser.Pesnocent, Is.EqualTo(initialBalances[user.Id]),
                    $"User {user.Id} should have original balance {initialBalances[user.Id]} after refund");
            }

            // Verify all requests are in ClosedDiscarded state
            using (var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository))
            {
                var allRequests = connection.Query<PostponeRequest>(@"SELECT * FROM PostponeRequest").ToArray();

                Assert.That(allRequests.Length, Is.EqualTo(2));

                foreach (var request in allRequests)
                {
                    Console.WriteLine($"Request {request.Id} from user {request.UserId}: state={request.State}, cost={request.CostPesnocents}");
                    Assert.That(request.State, Is.EqualTo(PostponeRequestState.ClosedDiscarded));
                }
            }
        }

        [Test]
        public async Task ShouldNotRefundWhenQuorumReached()
        {
            using var compartment = new TestCompartment(TestContext.CurrentContext);
            
            var config = compartment.Container.Resolve<IBotConfiguration>();

            compartment.Repository.UpdateState(state => state.State, ContestState.Contest);
            compartment.Repository.UpdateState(state => state.CurrentChallengeRoundNumber, 100);

            var initialDeadline = Instant.FromUtc(2025, 01, 01, 12, 00);
            compartment.Repository.UpdateState(state => state.NextDeadlineUTC, initialDeadline);

            var users = new List<User>();
            var initialBalances = new Dictionary<long, long>();

            // Create enough users to reach quorum
            for (int i = 0; i < config.PostponeQuorum; i++)
            {
                var fakeUser = compartment.Repository.CreateOrGetUserByTgIdentity(new Telegram.Bot.Types.User
                {
                    Id = i + 200,
                    FirstName = $"quorum-user-{i}",
                });

                compartment.Repository.AddPesnocentsToUser(fakeUser.Id, 700);

                fakeUser = compartment.Repository.GetExistingUserWithTgId(fakeUser.Id);
                users.Add(fakeUser);
                initialBalances[fakeUser.Id] = fakeUser.Pesnocent;

                // Create fake entry for previous round
                compartment.Repository.GetOrCreateContestEntry(fakeUser, 1, 1, 1, 99, out _);
            }

            var postponeService = compartment.Container.Resolve<PostponeService>();

            // Create requests up to quorum (last one triggers postpone)
            for (int i = 0; i < config.PostponeQuorum; i++)
            {
                var result = await postponeService.DemandPostponeRequest(users[i], Duration.FromHours(2));

                if (i < config.PostponeQuorum - 1)
                {
                    Assert.That(result, Is.EqualTo(PostponeService.PostponeResult.Accepted));
                }
                else
                {
                    Assert.That(result, Is.EqualTo(PostponeService.PostponeResult.AcceptedAndPostponed));
                }
            }

            // Verify deadline was changed
            Assert.That(compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC,
                Is.EqualTo(initialDeadline + Duration.FromHours(2)));

            // Verify coins were NOT refunded (one request satisfied, others discarded but no refund on quorum)
            using (var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository))
            {
                var allRequests = connection.Query<PostponeRequest>(@"SELECT * FROM PostponeRequest").ToArray();

                Assert.That(allRequests.Length, Is.EqualTo(config.PostponeQuorum));

                var satisfiedCount = allRequests.Count(r => r.State == PostponeRequestState.ClosedSatisfied);
                var discardedCount = allRequests.Count(r => r.State == PostponeRequestState.ClosedDiscarded);

                Assert.That(satisfiedCount, Is.EqualTo(1), "Exactly one request should be satisfied");
                Assert.That(discardedCount, Is.EqualTo(config.PostponeQuorum - 1), "Other requests should be discarded");

                // Verify NO user got refunded (all still have 600 pesnocents after deduction)
                foreach (var user in users)
                {
                    var finalUser = compartment.Repository.GetExistingUserWithTgId(user.Id);
                    Assert.That(finalUser.Pesnocent, Is.EqualTo(600),
                        $"User {user.Id} should NOT be refunded when quorum reached (should have 700 - 100 = 600)");
                }
            }
        }

        [Test]
        public async Task ShouldHandleRefundEdgeCases()
        {
            using var compartment = new TestCompartment(TestContext.CurrentContext);
            
            compartment.Repository.UpdateState(state => state.State, ContestState.Contest);
            compartment.Repository.UpdateState(state => state.CurrentChallengeRoundNumber, 101);

            // Case 1: User with ChatId = null (blocked bot)
            var userWithoutChat = compartment.Repository.CreateOrGetUserByTgIdentity(new Telegram.Bot.Types.User
            {
                Id = 300,
                FirstName = "no-chat-user",
            });
            compartment.Repository.AddPesnocentsToUser(userWithoutChat.Id, 700);
            userWithoutChat = compartment.Repository.GetExistingUserWithTgId(userWithoutChat.Id);
            userWithoutChat.ChatId = null; // No chat
            compartment.Repository.UpdateUser(userWithoutChat, 0);
            compartment.Repository.GetOrCreateContestEntry(userWithoutChat, 1, 1, 1, 100, out _);

            // Case 2: User with ChatId (normal case)
            var userWithChat = compartment.Repository.CreateOrGetUserByTgIdentity(new Telegram.Bot.Types.User
            {
                Id = 301,
                FirstName = "normal-user",
            });
            compartment.Repository.AddPesnocentsToUser(userWithChat.Id, 700);
            userWithChat = compartment.Repository.GetExistingUserWithTgId(userWithChat.Id);
            userWithChat.ChatId = 12345;
            compartment.Repository.UpdateUser(userWithChat, 0);
            compartment.Repository.GetOrCreateContestEntry(userWithChat, 1, 1, 1, 100, out _);

            var postponeService = compartment.Container.Resolve<PostponeService>();

            // Both users create requests
            await postponeService.DemandPostponeRequest(userWithoutChat, Duration.FromHours(1));
            await postponeService.DemandPostponeRequest(userWithChat, Duration.FromHours(1));

            // Close and refund
            await postponeService.CloseRefundAllPostponeRequests(PostponeRequestState.ClosedDiscarded);

            // Both users should get refunds regardless of ChatId
            var refundedUser1 = compartment.Repository.GetExistingUserWithTgId(userWithoutChat.Id);
            var refundedUser2 = compartment.Repository.GetExistingUserWithTgId(userWithChat.Id);

            Assert.That(refundedUser1.Pesnocent, Is.EqualTo(700), "User without ChatId should still get refund (500 + 200 back to original)");
            Assert.That(refundedUser2.Pesnocent, Is.EqualTo(700), "User with ChatId should get refund (500 + 200 back to original)");
        }

        [Test]
        public async Task ShouldHandleNoRequestsToRefund()
        {
            using var compartment = new TestCompartment(TestContext.CurrentContext);
            
            compartment.Repository.UpdateState(state => state.State, ContestState.Contest);

            var postponeService = compartment.Container.Resolve<PostponeService>();

            // Call with no open requests
            await postponeService.CloseRefundAllPostponeRequests(PostponeRequestState.ClosedDiscarded);

            // Should complete without errors
            using (var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository))
            {
                var allRequests = connection.Query<PostponeRequest>(@"SELECT * FROM PostponeRequest").ToArray();
                Assert.That(allRequests.Length, Is.EqualTo(0));
            }
        }

    }
}