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

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Please send task template in next message (you have 1 hour)");

            var response = await dialog.GetMessageInThreadAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(60)).Token);

            if (null == response)
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, ":(");
                return;
            }

            await _controller.KickstartContestAsync(response.Text, user);

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Looks like all OK");

            logger.Info($"Next round task poll kickstarted");
        }
    }
}
