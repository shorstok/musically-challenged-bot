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
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Commands
{
    public class DescribeContestEntryCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly BotConfiguration _configuration;
        private readonly ContestController _contestController;
        private readonly LocStrings _loc;

        public string CommandName { get; } = Scheme.DescribeCommandName;
        public string UserFriendlyDescription => _loc.DescribeContestEntryCommandHandler_Description;

        private static readonly ILog logger = Log.Get(typeof(DescribeContestEntryCommandHandler));

        public DescribeContestEntryCommandHandler(IRepository repository,
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

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to submit contest entry description");

            if (state.State != ContestState.Contest)
            {
                logger.Info($"Bot in state {state.State}, denied");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                    _loc.DescribeContestEntryCommandHandler_OnlyAvailableInContestState);
                return;
            }

            var entry = _repository.GetActiveContestEntryForUser(user.Id);

            if (null == entry)
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                    _loc.DescribeContestEntryCommandHandler_SendEntryFirst);
                return;
            }

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                _loc.DescribeContestEntryCommandHandler_SubmitGuidelines);

            var response = await dialog.GetMessageInThreadAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(_configuration.SubmissionTimeoutMinutes)).Token);


            if (!await ValidateContestMessage(response, dialog))
            {
                logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} failed description validation");
                return;
            }

            var text = ContestController.EscapeTgHtml(response.Text);

            entry.Description =text;

            _repository.UpdateContestEntry(entry);
            
            await _contestController.UpdateContestEntry(user, entry);

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                _loc.DescribeContestEntryCommandHandler_SubmissionSucceeded);

            logger.Info($"Contest entry description submitted");
        }

        private async Task<bool> ValidateContestMessage(Message message, Dialog dialog)
        {
            var text = message.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                    _loc.DescribeContestEntryCommandHandler_SubmissionFailed);
                return false;
            }

            if (text.Length > 512)
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                    _loc.DescribeContestEntryCommandHandler_SubmissionTooLong);
                return false;
            }

            return true;
        }
    }
}