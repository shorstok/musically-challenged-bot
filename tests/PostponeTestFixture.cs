using System;
using System.Collections.Generic;
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

            }
        }
    }
}