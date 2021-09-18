using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using musicallychallenged.Services.Telegram;
using NodaTime;
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
        public GenericUserScenarios GenericScenarios { get; private set; }
        public IBotConfiguration Configuration { get; private set; }
        public TweakableClockService Clock { get; private set; }


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

        internal static DbConnection GetRepositoryDbConnection(IRepository repository)
        {
            if (repository == null) 
                throw new ArgumentNullException(nameof(repository));

            //This (CreateOpenConnection) should be inaccessible from outside, but currently we have no way
            //to set user credentials (they are set externally, in DB)
            //so we have to use reflection to get connection to DB and execute
            //sql command to set it manually

            var createOpenConnectionMethodInfo = repository.GetType().GetMethod("CreateOpenConnection",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (null == createOpenConnectionMethodInfo)
                throw new Exception(
                    $"Could not find CreateOpenConnection method in {repository?.GetType()}, maybe it was refactored?");

            return createOpenConnectionMethodInfo.Invoke(repository, null) as DbConnection;
        }

        public async Task<bool> WaitTillStateMatches(Expression<Predicate<SystemState>> predicate, long? timeoutMs = 1000)
        {
            if (Debugger.IsAttached)
                timeoutMs = 10 * 60 * 1000;

            var transitionStopwatch = Stopwatch.StartNew();
            var compiledPredicate = predicate.Compile();

            do
            {
                var state = Repository.GetOrCreateCurrentState();

                if (compiledPredicate(state))
                {
                    Logger.Info($"State switch - satisfied predicate {(predicate.Body.ToString())} in {transitionStopwatch.ElapsedMilliseconds}ms");
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
            Configuration = Container.Resolve<IBotConfiguration>();
            ScenarioController = Container.Resolve<UserScenarioController>();
            GenericScenarios = Container.Resolve<GenericUserScenarios>();
            Clock = (TweakableClockService)Container.Resolve<IClock>();
        }
        

        private void RunInMemorySqliteMigrations(string connectionString)
        {
            Logger.Info("Applying migrations for in-memory sqlite db...");

            new AdHocMigrationRunner(connectionString).RunMigrations();

            Logger.Info("Applied migrations for in-memory sqlite db");
        }

        private void BuildMockContainer()
        {
            var containerBuilder = new ContainerBuilder();

            containerBuilder.RegisterModule<ProductionModule>();
            containerBuilder.RegisterModule<MockModule>();

            // Registering an InMemorySqliteRepository instance separately to be able to run migrations before the container is built
            var clock = new TweakableClockService();
            var inMemorySqlite = new InMemorySqliteRepository(clock);
            RunInMemorySqliteMigrations(inMemorySqlite.GetInMemoryConnectionString());

            containerBuilder.RegisterInstance(clock).As<IClock>().SingleInstance();
            containerBuilder.RegisterInstance(inMemorySqlite).As<IRepository>().SingleInstance();

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
