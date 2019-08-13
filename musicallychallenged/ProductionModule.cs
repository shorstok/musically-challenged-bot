using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using musicallychallenged.Commands;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Localization;
using musicallychallenged.Services;
using musicallychallenged.Services.Events;
using musicallychallenged.Services.Telegram;
using NodaTime;

namespace musicallychallenged
{
    internal class ProductionModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterInstance(BotConfiguration.LoadOrCreate(true)).AsSelf().SingleInstance();
            
            builder.RegisterType<LocStrings>().AsSelf().SingleInstance();           
            
            builder.RegisterType<NewTaskSelectorController>().AsSelf().InstancePerDependency();
            builder.RegisterType<InnerCircleVotingController>().AsSelf().InstancePerDependency();
            
            builder.RegisterType<CrypticNameResolver>().AsSelf().SingleInstance();
            builder.RegisterType<StateController>().As<IStartable>().AsSelf().SingleInstance();

            builder.RegisterType<EventAggregator>().As<IEventAggregator>().SingleInstance();

            builder.RegisterType<ServiceHost>().AsSelf().SingleInstance();
            builder.RegisterType<VotingController>().AsSelf().SingleInstance();
            builder.RegisterType<ContestController>().AsSelf().SingleInstance();
            builder.RegisterType<CommandManager>().AsSelf().SingleInstance();
            builder.RegisterType<DialogManager>().AsSelf().SingleInstance();
            builder.RegisterType<BroadcastController>().AsSelf().SingleInstance();
            builder.RegisterType<SystemClockService>().As<IClock>().SingleInstance();
            builder.RegisterType<RandomTaskRepository>().AsSelf().SingleInstance();
            builder.RegisterType<TimeService>().AsSelf().SingleInstance();
            builder.RegisterType<PollingStateScheduler>().As<IStateScheduler>().SingleInstance();

            builder.RegisterType<SqliteRepository>().As<IRepository>().SingleInstance();
            builder.RegisterType<TelegramClient>().As<ITelegramClient>().SingleInstance();
            
            //Register all telegram command handlers

            builder.RegisterAssemblyTypes(typeof(ITelegramCommandHandler).Assembly)
                .Where(t => !t.IsAbstract && typeof(ITelegramCommandHandler).IsAssignableFrom(t))
                .As<ITelegramCommandHandler>()
                .SingleInstance();

            builder.RegisterAssemblyTypes(typeof(ITelegramQueryHandler).Assembly)
                .Where(t => !t.IsAbstract && typeof(ITelegramQueryHandler).IsAssignableFrom(t))
                .As<ITelegramQueryHandler>()
                .SingleInstance();
        }
    }
}