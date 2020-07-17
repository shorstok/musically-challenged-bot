using System;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Administration;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using musicallychallenged.Services.Events;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Types.ReplyMarkups;

namespace musicallychallenged.Commands
{
    [DemandCredentials(UserCredentials.Supervisor)]
    public class StandbyCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly BotConfiguration _configuration;
        private readonly IEventAggregator _eventAggregator;
        private readonly LocStrings _loc;

        public string CommandName { get; } =Scheme.StandbyCommandName;
        public string UserFriendlyDescription => _loc.StandbyCommandHandler_Description;

        private static readonly ILog logger = Log.Get(typeof(StandbyCommandHandler));

        public StandbyCommandHandler(IRepository repository, 
            BotConfiguration configuration,

            IEventAggregator eventAggregator,
            LocStrings loc)
        {
            _repository = repository;
            _configuration = configuration;
            _eventAggregator = eventAggregator;
            _loc = loc;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            var state = _repository.GetOrCreateCurrentState();

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to put bot in Standby");

            if (state.State == ContestState.Standby)
            {
                logger.Info($"Bot in state {state.State}, denied");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, $"Allowed only in `Contest/Voting` state, now in `{state.State}`");
                return;
            }

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Confirm your intentions -- bot goes to sleep?", 
                replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("YES","y")));

            var response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);

            if (response?.Data != "y")
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Cancelled");
                return;
            }
            
            await dialog.TelegramClient.AnswerCallbackQueryAsync(response.Id);
            
            _eventAggregator.Publish(new DemandStandbyEvent());

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Confirmed");
            
            logger.Info($"Standby event issued");
        }      
    }
}