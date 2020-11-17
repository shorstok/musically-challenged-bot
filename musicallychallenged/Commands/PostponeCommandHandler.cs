using System;
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
using NodaTime;
using Telegram.Bot.Types;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Commands
{
    public class PostponeCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly BotConfiguration _configuration;
        private readonly ContestController _contestController;
        private readonly PostponeService _postponeService;
        private readonly LocStrings _loc;

        public string CommandName { get; } = Schema.PostponeCommandName;
        public string UserFriendlyDescription => _loc.PostponeCommandHandler_Description;

        private static readonly ILog logger = Log.Get(typeof(PostponeCommandHandler));

        public PostponeCommandHandler(IRepository repository,
            BotConfiguration configuration,
            ContestController contestController,
            PostponeService postponeService,
            LocStrings loc)
        {
            _repository = repository;
            _configuration = configuration;
            _contestController = contestController;
            _postponeService = postponeService;
            _loc = loc;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            var state = _repository.GetOrCreateCurrentState();

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to request postpone");

            if (state.State != ContestState.Contest)
            {
                logger.Info($"Bot in state {state.State}, denied");
                
                await dialog.TelegramClient.SendTextMessageAsync(
                    dialog.ChatId,
                    _loc.CommandHandler_OnlyAvailableInContestState);
                
                return;
            }

            var count = _repository.GetFinishedContestEntryCountForUser(user.Id);

            if (count < 1)
            {
                logger.Info($"User has {count} previously submitted works, postpone denied");

                await dialog.TelegramClient.SendTextMessageAsync(
                    dialog.ChatId,
                    _loc.PostponeCommandHandler_OnlyForKnownUsers);

                return;
            }

            var result = await _postponeService.DemandPostponeRequest(user,Duration.FromMinutes(15));

            logger.Info($"Postpone request from {user.GetUsernameOrNameWithCircumflex()} submitted");
        }

    }
}