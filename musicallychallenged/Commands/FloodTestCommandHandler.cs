using System;
using System.Collections.Generic;
using System.Linq;
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
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Commands
{
    [DemandCredentials(UserCredentials.Supervisor)]
    public class FloodTestCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly VotingController _votingController;
        private readonly IEventAggregator _eventAggregator;
        private readonly LocStrings _loc;

        public string CommandName { get; } = "floodtest";
        public string UserFriendlyDescription => "dont";

        private static readonly ILog logger = Log.Get(typeof(FloodTestCommandHandler));

        public FloodTestCommandHandler(IRepository repository, 
            VotingController votingController,
            IEventAggregator eventAggregator,
            LocStrings loc)
        {
            _repository = repository;
            _votingController = votingController;

            _eventAggregator = eventAggregator;
            _loc = loc;
        }

        private Random _random = new Random();

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            var state = _repository.GetOrCreateCurrentState();

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to floodtest");

            var message = await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Confirm your intentions -- FLOOD CHATS?", 
                replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("YES","y")));

            var response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);

            if (response != null)
                await dialog.TelegramClient.AnswerCallbackQueryAsync(response.Id);

            await dialog.TelegramClient.EditMessageReplyMarkupAsync(message.Chat.Id,message.MessageId,replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove
            
            if (response?.Data != "y")
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Cancelled");
                return;
            }
            
            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Confirmed");

            await _votingController.CreateVotingStatsMessageAsync();

            await Task.Delay(1000);

            await _votingController.UpdateVotingStats();

            await Task.Delay(7000);

            await _votingController.UpdateVotingStats();

            //await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Deleted");
        }      
    }
}