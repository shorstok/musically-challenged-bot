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
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Types;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Commands
{
    [DemandCredentials(UserCredentials.Supervisor)]
    public class KickstartCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly BotConfiguration _configuration;
        private readonly ContestController _contestController;
        private readonly LocStrings _loc;

        public string CommandName { get; } = "kickstart";
        public string UserFriendlyDescription => _loc.KickstartCommandHandler_Description;

        private static readonly ILog logger = Log.Get(typeof(KickstartCommandHandler));

        public KickstartCommandHandler(IRepository repository, 
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

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to kickstart next challenge round");

            if (state.State != ContestState.Standby)
            {
                logger.Info($"Bot in state {state.State}, denied");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, $"Allowed only in `Standby` state, now in `{state.State}`");
                return;
            }

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Please send task template in next message (you have 1 hour)");

            var response = await dialog.GetMessageInThreadAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(60)).Token);

            if (null == response)
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,":(");
                return;
            }

            await _contestController.KickstartContestAsync(response.Text, user);

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Looks like all OK");
            
            logger.Info($"Contest kickstarted");
        }      
    }
}