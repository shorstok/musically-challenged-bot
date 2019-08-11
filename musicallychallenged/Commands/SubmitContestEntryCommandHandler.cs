using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Commands
{
    public class SubmitContestEntryCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly BotConfiguration _configuration;
        private readonly ContestController _contestController;
        private readonly LocStrings _loc;

        public string CommandName { get; } = "submit";
        public string UserFriendlyDescription => _loc.SubmitContestEntryCommandHandler_Description;

        private static readonly ILog logger = Log.Get(typeof(SubmitContestEntryCommandHandler));

        public SubmitContestEntryCommandHandler(IRepository repository, 
            BotConfiguration configuration,
            ContestController contestController,
            LocStrings loc)
        {
            _repository = repository;
            _configuration = configuration;
            _contestController = contestController;
            _loc = loc;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            var state = _repository.GetOrCreateCurrentState();

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to submit contest entry");

            if (state.State != ContestState.Contest)
            {
                logger.Info($"Bot in state {state.State}, denied");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, _loc.SubmitContestEntryCommandHandler_OnlyAvailableInContestState);
                return;
            }

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, _loc.SubmitContestEntryCommandHandler_SubmitGuidelines);

            var response = await dialog.GetMessageInThreadAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(_configuration.SubmissionTimeoutMinutes)).Token);
            
            if(!isValidContestMessage(response))
            {
                logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} failed sumbission validation");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, _loc.SubmitContestEntryCommandHandler_SubmissionFailed);
                return;
            }

            await _contestController.SubmitNewEntry(response, user);

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, _loc.SubmitContestEntryCommandHandler_SubmissionSucceeded);
            
            logger.Info($"Contest entry submitted");
        }

        private bool isValidContestMessage(Message message)
        {
            if (message.Audio != null || message.Video != null)
                return true;

            if (message.Entities?.Any(e => e.Type == MessageEntityType.Url) == true)
                return true;

            return false;
        }

    }
}