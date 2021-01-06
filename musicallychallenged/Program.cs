using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using musicallychallenged.Services.Telegram;
using Topshelf;

namespace musicallychallenged
{
    class Program
    {
        private static readonly ILog logger = Log.Get(typeof(Program));

        private static IContainer CreateDiContainer()
        {
            var containerBuilder = new ContainerBuilder();

            containerBuilder.RegisterInstance(BotConfiguration.LoadOrCreate(true)).As<IBotConfiguration>().SingleInstance();

            containerBuilder.RegisterModule<ProductionModule>();

            return containerBuilder.Build();
        }

        private static void Main(string[] args)
        {
            logger.Info($"Started {DateTime.Now}");

            PathService.EnsurePathExists();

            logger.Info($"Service data resides in `{PathService.AppData}`");


            IContainer container;

            try
            {
                RunDbMigrations();

                container = CreateDiContainer();
            }
            catch (Exception e)
            {
                logger.Fatal($"Startup error",e);

                Environment.Exit(-1);
                return;
            }

            HostFactory.Run(configurator =>
            {
                configurator.UseLog4Net();
                configurator.StartAutomatically();

                configurator.EnableServiceRecovery(rc =>
                {
                    rc.RestartService(1); // restart the service after 1 minute
                });

                configurator.Service<ServiceHost>(s =>
                {
                    s.ConstructUsing(hostSettings => container.Resolve<ServiceHost>());
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Stop());
                });

                configurator.RunAsLocalSystem();

                configurator.SetDescription("Telegram Music Challenge Bot host");
                configurator.SetDisplayName("Telegram Music Challenge Bot");
                configurator.SetServiceName("MusicChallengeBot");
            });
        }

        private static void RunDbMigrations()
        {
            var connectionString = SqliteRepository.CreateConnectionString();

            logger.Info($"Running migrations for `{connectionString}`...");

            new AdHocMigrationRunner(connectionString).RunMigrations();
        }
    }
}
