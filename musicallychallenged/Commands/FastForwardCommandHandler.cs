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
using musicallychallenged.Services.Events;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Commands
{
    [DemandCredentials(UserCredentials.Supervisor)]
    public class FastForwardCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly BotConfiguration _configuration;
        private readonly IEventAggregator _eventAggregator;
        private readonly LocStrings _loc;

        public string CommandName { get; } = Scheme.FastForwardCommandName;
        public string UserFriendlyDescription => _loc.FastForwardCommandHandler_Description;

        private static readonly ILog logger = Log.Get(typeof(FastForwardCommandHandler));

        public FastForwardCommandHandler(IRepository repository, 
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

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to ffwd");

            if (state.State != ContestState.Contest  && state.State != ContestState.Voting)
            {
                logger.Info($"Bot in state {state.State}, denied");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, $"Allowed only in `Contest/Voting` state, now in `{state.State}`");
                return;
            }

            var message = await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"What do you want to do", 
                replyMarkup: new InlineKeyboardMarkup(new []
                {
                    InlineKeyboardButton.WithCallbackData("PreDeadine","pdl"),
                    InlineKeyboardButton.WithCallbackData("Deadline","dl"),
                }));

            var response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);

            if (response != null)
                await dialog.TelegramClient.AnswerCallbackQueryAsync(response.Id);

            await dialog.TelegramClient.EditMessageReplyMarkupAsync(message.Chat.Id,message.MessageId,replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove

            switch (response)
            {
                case null:
                    await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Cancelled");
                    return;
                case CallbackQuery query:
                    
                    if(query.Data == "pdl")
                        _eventAggregator.Publish(new DemandFastForwardEvent(true));
                    else if(query.Data == "dl")
                        _eventAggregator.Publish(new DemandFastForwardEvent(false));

                    break;

            }

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Confirmed");
            
            logger.Info($"DemandFastForwardEvent issued");
        }      
    }
}