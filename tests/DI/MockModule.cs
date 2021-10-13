using Autofac;
using log4net;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using musicallychallenged.Config;
using musicallychallenged.Services.Sync;
using tests.Mockups;
using tests.Mockups.Messaging;

namespace tests.DI
{
    class MockModule : Module
    {
        private static readonly ILog Logger = Log.Get(typeof(MockModule));

        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterInstance(MockConfiguration.Snapshot).As<IBotConfiguration>().SingleInstance();

            builder.RegisterType<MockTelegramClient>().AsSelf().As<ITelegramClient>().SingleInstance();
            builder.RegisterType<UserScenarioContext>().AsSelf().InstancePerDependency();
            builder.RegisterType<UserScenarioController>().AsSelf().SingleInstance();
            builder.RegisterType<GenericUserScenarios>().AsSelf().SingleInstance();
            builder.RegisterType<MockMessageMediatorService>().AsSelf().SingleInstance();
            builder.RegisterType<MockIngestService>().AsSelf().As<IPesnocloudIngestService>().SingleInstance();
        }
    }
}
