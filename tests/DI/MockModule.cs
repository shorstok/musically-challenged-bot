using System;
using System.Threading.Tasks;
using Autofac;
using musicallychallenged.Data;
using musicallychallenged.Services.Telegram;
using tests.Mockups;

namespace tests.DI
{
    class MockModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterInstance(MockConfiguration.Snapshot).AsSelf().SingleInstance();

            builder.RegisterType<InMemorySqliteRepository>().As<IRepository>().SingleInstance();
            builder.RegisterType<MockTelegramClient>().AsSelf().As<ITelegramClient>().SingleInstance();
            builder.RegisterType<UserScenarioContext>().AsSelf().InstancePerDependency();
            builder.RegisterType<UserScenarioController>().AsSelf().SingleInstance();
            builder.RegisterType<GenericUserScenarios>().AsSelf().SingleInstance();
        }
    }
}
