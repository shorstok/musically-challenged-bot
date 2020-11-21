﻿using System;
using System.Collections.Generic;
using System.Linq;
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
            using (var compartment = new TestCompartment())
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
        public async Task ShouldLimitPostponeAmountForOneRound()
        {
            using (var compartment = new TestCompartment())
            {
                //Setup
                var config = compartment.Container.Resolve<BotConfiguration>();

                compartment.Repository.UpdateState(state => state.CurrentChallengeRoundNumber, 102);

                await compartment.ScenarioController.StartUserScenario(
                    compartment.GenericScenarios.SupervisorKickstartContest,
                    UserCredentials.Supervisor).ScenarioTask;

                Assert.That(await compartment.WaitTillStateMatches(state => state.CurrentTaskMessagelId != null),
                    Is.True,
                    "Failed kickstarting contest (message id not set)");

                var initialDeadline = compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC;

                //Run generic users

                Logger.Info("Running 'submit' scenarios");

                var users = Enumerable.Range(0, 10)
                    .Select(t => compartment
                        .ScenarioController
                        .StartUserScenario(async context =>
                        {
                            await compartment.GenericScenarios.ContesterUserScenario(context);
                            
                            //Create fake entry for previous round

                            var user = compartment.Repository.CreateOrGetUserByTgIdentity(context.MockUser);
                            var state = compartment.Repository.GetOrCreateCurrentState();

                            context.PersistUserChatId();

                            compartment.Repository.GetOrCreateContestEntry(user,
                                1,1,1,
                                state.CurrentChallengeRoundNumber-1,
                                out _);

                            var entry = compartment.Repository.GetActiveContestEntryForUser(user.Id);
                            entry.ConsolidatedVoteCount = 10; //make entry finished
                            compartment.Repository.UpdateContestEntry(entry);

                        })).ToArray();

                await Task.WhenAll(users.Select(u => u.ScenarioTask)); //wait for all user scenarios to complete

                Assert.That(compartment.Repository.GetOrCreateCurrentState().NextDeadlineUTC, Is.EqualTo(initialDeadline));

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
        

        [Test]
        public async Task ShouldDiscardAllOpenRequests()
        {
            using (var compartment = new TestCompartment())
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

                await postponeService.CloseAllPostponeRequests(PostponeRequestState.ClosedDiscarded);

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
            using (var compartment = new TestCompartment())
            {
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
    }
}