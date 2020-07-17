using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using NUnit.Framework;
using tests.DI;
using tests.Mockups;

namespace tests
{
    [TestFixture]
    public class WelcomeTextTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(WelcomeTextTestFixture));

        [Test]
        public async Task ShouldSendUsageCasesForAnonymousUser()
        {
            using (var compartment = new MockupTgCompartment())
            {
                async Task UserScenario(UserScenarioContext context)
                {
                    context.SendMessage("/nonexistent-command-"+Guid.NewGuid(), MockConfiguration.MainChat);

                    var answer = await context.ReadTillMessageReceived();

                    var header = LocTokens.SubstituteTokens(
                        compartment.Localization.UnknownCommandUsageTemplate,
                        Tuple.Create(LocTokens.Details,
                            string.Empty));

                    var description = answer.Text.Replace(header, string.Empty);

                    var user = compartment.Repository.CreateOrGetUserByTgIdentity(context.MockUser);

                    var allCommands = compartment.Container.Resolve<CommandManager>().GetAvailableCommandHandlers(user).ToArray();

                    Assert.That(answer?.Text, Contains.Substring(header), "Unknown command response should contain general usage pretext");

                    foreach (var commandHandler in allCommands)
                    {
                        Assert.That(description, Contains.Substring("/"+commandHandler.CommandName),
                            $"Unknown command response for anonymous user should {commandHandler.CommandName} usecase");
                        Assert.That(description, Contains.Substring(commandHandler.UserFriendlyDescription),
                            $"Unknown command response for anonymous user should {commandHandler.CommandName} description " +
                            $"({commandHandler.UserFriendlyDescription})");
                    }
                }

                var ctx = compartment.StartUserScenario(UserScenario);

                await ctx.ScenarioTask; //wait for user scenario to complete
            }
        }

        [Test]
        public async Task ShouldSendUsageCasesForAdministrativeUser()
        {
            var setupComplete = new TaskCompletionSource<object>();
            
            using (var compartment = new MockupTgCompartment())
            {
                async Task UserScenario(UserScenarioContext context)
                {
                    await setupComplete.Task;

                    context.SendMessage("/nonexistent-command-"+Guid.NewGuid(), MockConfiguration.MainChat);

                    var answer = await context.ReadTillMessageReceived();

                    var header = LocTokens.SubstituteTokens(
                        compartment.Localization.UnknownCommandUsageTemplate,
                        Tuple.Create(LocTokens.Details,
                            string.Empty));

                    var description = answer.Text.Replace(header, string.Empty);

                    var user = compartment.Repository.CreateOrGetUserByTgIdentity(context.MockUser);

                    var allCommands = compartment.Container.Resolve<CommandManager>().GetAvailableCommandHandlers(user).ToArray();

                    Assert.That(answer?.Text, Contains.Substring(header), "Unknown command response should contain general usage pretext");

                    foreach (var commandHandler in allCommands)
                    {
                        Assert.That(description, Contains.Substring("/"+commandHandler.CommandName),
                            $"Unknown command response for admin user should contain {commandHandler.CommandName} usecase");
                        Assert.That(description, Contains.Substring(commandHandler.UserFriendlyDescription),
                            $"Unknown command response for admin user should contain {commandHandler.CommandName} description " +
                            $"({commandHandler.UserFriendlyDescription})");
                    }

                    Logger.Info($"Ok, administrative response contains [{string.Join(", ",allCommands.Select(q=>q.CommandName))}] commands");
                }

                var ctx = compartment.StartUserScenario(UserScenario, UserCredentials.Supervisor);

                setupComplete.SetResult(true);

                await ctx.ScenarioTask; //wait for user scenario to complete
            }
        }

    }
}
