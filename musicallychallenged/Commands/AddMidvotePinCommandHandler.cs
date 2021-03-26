using System;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Administration;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using musicallychallenged.Services.Telegram;

namespace musicallychallenged.Commands
{
    [DemandCredentials(UserCredentials.Supervisor)]
    public class AddMidvotePinCommandHandler : ITelegramCommandHandler
    {
        private readonly MidvoteEntryController _midvoteEntryController;
        private readonly LocStrings _loc;

        public string CommandName { get; } = Schema.AddMidvotePin;
        public string UserFriendlyDescription => _loc.AddMidvotePinCommandHandler_Description;

        private static readonly ILog logger = Log.Get(typeof(AddMidvotePinCommandHandler));

        public AddMidvotePinCommandHandler(MidvoteEntryController midvoteEntryController,
            LocStrings loc)
        {
            _midvoteEntryController = midvoteEntryController;
            _loc = loc;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to add midvote pin");
            
            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Please send midvote pin");

            var response = await dialog.GetMessageInThreadAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(60)).Token);

            if (null == response)
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,":(");
                return;
            }

            int count = await _midvoteEntryController.CreateMidvotePin(response.Text);

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, $"Pin added. {count} active pins total");
            
            logger.Info($"Midvote pin added");
        }      
    }
}