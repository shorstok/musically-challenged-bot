using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using tests.Mockups;

namespace tests.DI
{
    public class TestCompartment : IDisposable
    {
        private static readonly ILog Logger = Log.Get(typeof(TestCompartment));

        public IContainer Container { get; private set; }

        private ServiceHost _serviceHost;
        public IRepository Repository { get; set; }
        public LocStrings Localization { get; private set; }
        public UserScenarioController ScenarioController { get; private set; }

        public TestCompartment()
        {
            BuildMockContainer();
            ResolveServices();
            RunMockService();
        }

        private void RunMockService()
        {
            //Setup defaults

            Repository.UpdateState(state => state.VotingChannelId, MockConfiguration.VotingChat.Id);
            Repository.UpdateState(state => state.MainChannelId, MockConfiguration.MainChat.Id);
            Repository.UpdateState(state => state.ContestDurationDays, 7);
            Repository.UpdateState(state => state.VotingDurationDays, 2);

            _serviceHost.Start();
        }

        public async Task<bool> WaitTillStateTransition(ContestState targetState, long? timeoutMs = 1000)
        {
            var transitionStopwatch = Stopwatch.StartNew();

            do
            {
                var state = Repository.GetOrCreateCurrentState();

                if (state.State == targetState)
                {
                    Logger.Info($"Transitioned to {targetState} state in {transitionStopwatch.ElapsedMilliseconds}ms");
                    return true;
                }

                await Task.Delay(100).ConfigureAwait(false);

            } while (transitionStopwatch.ElapsedMilliseconds < timeoutMs);

            return false;
        }

        private void ResolveServices()
        {
            _serviceHost = Container.Resolve<ServiceHost>();
            Repository = Container.Resolve<IRepository>();
            Localization = Container.Resolve<LocStrings>();
            ScenarioController = Container.Resolve<UserScenarioController>();
        }


        private void BuildMockContainer()
        {
            var containerBuilder = new ContainerBuilder();

            containerBuilder.RegisterModule<ProductionModule>();
            containerBuilder.RegisterModule<MockModule>();

            containerBuilder.RegisterInstance(this).AsSelf().SingleInstance();

            Container = containerBuilder.Build();
        }

        internal static UpdateEventArgs CreateMockUpdateEvent(Update source)
        {
            var type = typeof(UpdateEventArgs);

            var constructor = type.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[]
                {
                    typeof(Update)
                }, null);

            return (UpdateEventArgs) constructor.Invoke(new object[] {source});
        }

        public void Dispose()
        {
            _serviceHost.Stop();

            Container.Dispose();
        }
    }
}
