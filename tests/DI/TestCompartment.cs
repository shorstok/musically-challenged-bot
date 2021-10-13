using System;
using System.Data.Common;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Dapper;
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
using NUnit.Framework;
using Telegram.Bot.Types;
using tests.Mockups;
using User = Telegram.Bot.Types.User;

namespace tests.DI
{
    public partial class TestCompartment : IDisposable
    {
        private static readonly ILog Logger = Log.Get(typeof(TestCompartment));

        public IContainer Container { get; private set; }

        private ServiceHost _serviceHost;
        public IRepository Repository { get; private set; }
        public StateController StateController { get; private set; }
        public LocStrings Localization { get; private set; }
        public UserScenarioController ScenarioController { get; private set; }
        public GenericUserScenarios GenericScenarios { get; private set; }
        public IBotConfiguration Configuration { get; private set; }
        public TweakableClockService Clock { get; private set; }
        public ITelegramClient TelegramClient { get; private set; }


        public TestCompartment(TestContext currentContext)
        {
            BuildMockContainer(currentContext);
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

   

        public User GetCurrentWinnerDirect()
        {
            using var connection = GetRepositoryDbConnection(Repository);

            return connection.Query<User>("select u.* from User u inner join SystemState se on se.CurrentWinnerId = u.Id").FirstOrDefault();
        }

        public async Task<bool> WaitTillStateMatches(Expression<Predicate<SystemState>> predicate,
            bool ensureTransitionEnded,
            long? timeoutMs = 1000)
        {
            if (Debugger.IsAttached)
                timeoutMs = 10 * 60 * 1000;

            var transitionStopwatch = Stopwatch.StartNew();
            var compiledPredicate = predicate.Compile();

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs??1000));

            do
            {
                var state = Repository.GetOrCreateCurrentState();

                if (compiledPredicate(state))
                {
                    Logger.Info(
                        $"State switch - satisfied predicate {(predicate.Body.ToString())} in {transitionStopwatch.ElapsedMilliseconds}ms");

                    if(ensureTransitionEnded)
                    {
                        if (!await StateController.YieldTransitionComplete(cts.Token))
                            Logger.Warn($"Wait till transition end - timeout!");
                    }            
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
            StateController = Container.Resolve<StateController>();
            Localization = Container.Resolve<LocStrings>();
            Configuration = Container.Resolve<IBotConfiguration>();
            ScenarioController = Container.Resolve<UserScenarioController>();
            GenericScenarios = Container.Resolve<GenericUserScenarios>();
            TelegramClient = Container.Resolve<ITelegramClient>();
            Clock = (TweakableClockService)Container.Resolve<IClock>();
        }


        private void RunSqliteMigrations(string connectionString)
        {
            Logger.Info($"Applying migrations for in-memory sqlite db {connectionString}...");

            var cfgResourceName = typeof(RepositoryBase).Assembly.GetManifestResourceNames()
                .FirstOrDefault(e => e.IndexOf("bot.sqlite.sql", StringComparison.Ordinal) != -1);

            string schema = null;

            if (cfgResourceName == null)
            {
                Logger.Error($"Key `bot.sqlite.sql` not found in embedded resources");
                Assert.Fail();
            }

            using (var stream = typeof(RepositoryBase).Assembly.GetManifestResourceStream(cfgResourceName))
            {
                if (null == stream)
                {
                    Logger.Error($"Error opening resource `{cfgResourceName}`");
                    return;
                }

                using (var sr = new StreamReader(stream))
                    schema = sr.ReadToEnd();
            }

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Execute(schema);
            }

            new AdHocMigrationRunner(connectionString, false).RunMigrations();

            Logger.Info("Applied migrations for in-memory sqlite db");
        }

        private void BuildMockContainer(TestContext currentContext)
        {
            var containerBuilder = new ContainerBuilder();

            containerBuilder.RegisterModule<ProductionModule>();
            containerBuilder.RegisterModule<MockModule>();

            // Registering an InMemorySqliteRepository instance separately to be able to run migrations before the container is built
            var clock = new TweakableClockService();
            var testSqlite = new TestSqliteRepository(currentContext, clock);
            RunSqliteMigrations(testSqlite.CreateConnectionString());

            containerBuilder.RegisterInstance(clock).As<IClock>().SingleInstance();
            containerBuilder.RegisterInstance(testSqlite).As<IRepository>().SingleInstance();

            containerBuilder.RegisterInstance(this).AsSelf().SingleInstance();

            Container = containerBuilder.Build();
        }

        

        public void Dispose()
        {
            _serviceHost.Stop();

            Container.Dispose();
        }

    }
}