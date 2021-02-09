using log4net;
using musicallychallenged.Administration;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using musicallychallenged.Services.Telegram;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types.ReplyMarkups;

namespace musicallychallenged.Commands
{
    [DemandCredentials(UserCredentials.Supervisor)]
    class KickstartNextRoundTaskPollCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly LocStrings _loc;
        private readonly NextRoundTaskPollController _controller;

        private static readonly ILog logger = Log.Get(typeof(KickstartCommandHandler));

        public string CommandName { get; } = Schema.KickstartNextRoundTaskPollCommandName;
        public string UserFriendlyDescription => _loc.KickstartNextRoundTaskPollCommandHandler_Description;

        public KickstartNextRoundTaskPollCommandHandler(
            IRepository repository, 
            LocStrings loc, 
            NextRoundTaskPollController controller)
        {
            _repository = repository;
            _loc = loc;
            _controller = controller;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            var state = _repository.GetOrCreateCurrentState();

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to kickstart next round task poll");

            if (state.State != ContestState.Standby)
            {
                logger.Info($"Bot in state {state.State}, denied");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, $"Allowed only in `Standby` state, now in `{state.State}`");
                return;
            }

            var message = await dialog.TelegramClient.SendTextMessageAsync(
                dialog.ChatId,
                "Confirm your intentions -- kickstart task poll?", 
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("YES","y"),
                    InlineKeyboardButton.WithCallbackData("NO","n")
                }));

            var response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);

            //remove buttons
            await dialog.TelegramClient.EditMessageReplyMarkupAsync(
                message.Chat.Id,message.MessageId,
                replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); 

            if (response?.Data != "y")
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Cancelled");
                return;
            }
            
            await dialog.TelegramClient.AnswerCallbackQueryAsync(response.Id);           
            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Confirmed");

            await _controller.KickstartContestAsync(user);

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Looks like all OK");

            logger.Info($"Next round task poll kickstarted");
        }
    }
}
