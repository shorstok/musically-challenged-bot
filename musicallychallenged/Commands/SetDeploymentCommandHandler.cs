using System;
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
using musicallychallenged.Services.Telegram;
using NodaTime;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace musicallychallenged.Commands
{
    [DemandCredentials(UserCredentials.Supervisor)]
    public class SetDeploymentCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly IBotConfiguration _configuration;
        private readonly IClock _clock;
        private readonly TimeService _timeService;
        private readonly ContestController _contestController;
        private readonly LocStrings _loc;

        public string CommandName { get; } = Schema.DeployCommandName;
        public string UserFriendlyDescription => "Choose where bot is active";

        private static readonly ILog logger = Log.Get(typeof(SetDeploymentCommandHandler));

        public SetDeploymentCommandHandler(IRepository repository,
            IBotConfiguration configuration,
            IClock clock,
            TimeService timeService,
            ContestController contestController,
            LocStrings loc)
        {
            _repository = repository;
            _configuration = configuration;
            _clock = clock;
            _timeService = timeService;
            _contestController = contestController;
            _loc = loc;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            var state = _repository.GetOrCreateCurrentState();

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to deploy bot");

            var message = await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Confirm your intentions -- change bot deploymey? It would disrupt current active contests etc!", 
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("YES","y"),
                    InlineKeyboardButton.WithCallbackData("NO","n")
                }));

            var response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);

            await dialog.TelegramClient.EditMessageReplyMarkupAsync(message.Chat.Id,message.MessageId,
                replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove

            if (response?.Data != "y")
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Cancelled");
                return;
            }
            
            await dialog.TelegramClient.AnswerCallbackQueryAsync(response.Id);           
            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Confirmed");

            var current = _configuration.Deployments.FirstOrDefault(d =>
                d.VotingChatId == state.VotingChannelId && d.MainChatId == state.MainChannelId);


            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                $"Select deployment target. Bot is currently in <code>{current?.Name??"unkown"}</code>", 
                parseMode: ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(_configuration.Deployments.Select(d=>InlineKeyboardButton.WithCallbackData(d.Name,d.Name))));

            response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);

            await dialog.TelegramClient.EditMessageReplyMarkupAsync(message.Chat.Id,message.MessageId,
                replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove

            var match = _configuration.Deployments.FirstOrDefault(d => d.Name == response?.Data);

            if (match == null)
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Cancelled");
                return;
            }

            await dialog.TelegramClient.AnswerCallbackQueryAsync(response.Id);
            
            message = await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                $"Confirm your intentions -- deploy bot to <code>{match.Name}</code>? " +
                $"VotingId <code>{match.VotingChatId}</code>, MainId <code>{match.MainChatId}</code>", 
                ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("YES","y"),
                    InlineKeyboardButton.WithCallbackData("NO","n")
                }));

            response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);

            await dialog.TelegramClient.EditMessageReplyMarkupAsync(message.Chat.Id,message.MessageId,
                replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove

            if (response?.Data != "y")
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Cancelled");
                return;
            }
            
            _repository.UpdateState(x=>x.VotingChannelId,match.VotingChatId);           
            _repository.UpdateState(x=>x.MainChannelId,match.MainChatId);           

            await _contestController.UpdateCurrentTaskMessage();

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"It is done");
            logger.Info($"Deployed to {match.Name}: {match.VotingChatId}, {match.MainChatId}");
        }

        
    }
}