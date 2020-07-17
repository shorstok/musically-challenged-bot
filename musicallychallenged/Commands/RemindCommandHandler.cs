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
using NodaTime;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace musicallychallenged.Commands
{
    [DemandCredentials(UserCredentials.Supervisor)]
    public class RemindCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly BotConfiguration _configuration;
        private readonly IClock _clock;
        private readonly TimeService _timeService;
        private readonly VotingController _votingController;
        private readonly ContestController _contestController;
        private readonly LocStrings _loc;

        public string CommandName { get; } = Scheme.RemindCommandName; 
        public string UserFriendlyDescription => "Remind about deadline time";

        private static readonly ILog logger = Log.Get(typeof(RemindCommandHandler));

        public RemindCommandHandler(IRepository repository,
            BotConfiguration configuration,
            IClock clock,
            TimeService timeService,
            VotingController votingController,
            ContestController contestController,
            LocStrings loc)
        {
            _repository = repository;
            _configuration = configuration;
            _clock = clock;
            _timeService = timeService;
            _votingController = votingController;
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
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,$"Only available in contest state, now in {state.State}");
                return;
            }

            await _contestController.WarnAboutContestDeadlineSoon(false);
                  
            await _contestController.UpdateCurrentTaskMessage();
            await _votingController.UpdateCurrentTaskMessage();

            logger.Info($"Reminded OK");
        }
    }
}