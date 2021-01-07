using System;
using System.Collections.Generic;
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
using NodaTime;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Commands
{
    public class PostponeCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly IBotConfiguration _configuration;
        private readonly ContestController _contestController;
        private readonly PostponeService _postponeService;
        private readonly TimeService _timeService;
        private readonly LocStrings _loc;

        public string CommandName { get; } = Schema.PostponeCommandName;
        public string UserFriendlyDescription => _loc.PostponeCommandHandler_Description;

        private const string TokCancel = "C73B0385-E551-4515-94F7-301753B23A3B";

        private static readonly ILog logger = Log.Get(typeof(PostponeCommandHandler));

        public PostponeCommandHandler(IRepository repository,
            IBotConfiguration configuration,
            ContestController contestController,
            PostponeService postponeService,
            TimeService timeService,
            LocStrings loc)
        {
            _repository = repository;
            _configuration = configuration;
            _contestController = contestController;
            _postponeService = postponeService;
            _timeService = timeService;
            _loc = loc;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

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

            var availablePostponeOptions = new List<InlineKeyboardButton>();

            var mappedOptions =
                _configuration.PostponeOptions.ToDictionary(option => Guid.NewGuid().ToString(), option => option);

            var timeLeft = _timeService.GetTimeLeftTillDeadline();

            foreach (var postponeOption in mappedOptions)
            {
                //Show small durations only in last day
                if(timeLeft > Duration.FromDays(1) && 
                   postponeOption.Value.AsDuration < Duration.FromDays(1))
                    continue;

                availablePostponeOptions.Add(InlineKeyboardButton.WithCallbackData(
                    postponeOption.Value.GetLocalizedName(_loc),
                    postponeOption.Key));
            }

            availablePostponeOptions.Add(InlineKeyboardButton.WithCallbackData(_loc.CancelButtonLabel, TokCancel));

            var message = await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                _loc.PostponeCommandHandler_Preamble,
                ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(availablePostponeOptions));

            var response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);

            await dialog.TelegramClient.EditMessageReplyMarkupAsync(
                message.Chat.Id, 
                message.MessageId,
                replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove

            if (!mappedOptions.TryGetValue(response?.Data ?? string.Empty, out var selectedOption))
            {
                logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} cancelled postpone sequence");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, _loc.PostponeCommandHandler_Cancelled);
                return;
            }

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} selects {selectedOption.GetLocalizedName(_loc)} / {selectedOption.AsDuration}");

            var result = await _postponeService.DemandPostponeRequest(user, selectedOption.AsDuration);

            switch (result)
            {
                case PostponeService.PostponeResult.Accepted:
                    await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                        LocTokens.SubstituteTokens(_loc.PostponeCommandHandler_AcceptedTemplate, 
                            Tuple.Create(
                                LocTokens.Users, 
                                _configuration.PostponeQuorum.ToString())));
                    break;
                case PostponeService.PostponeResult.AcceptedAndPostponed:
                    await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                        LocTokens.SubstituteTokens(_loc.PostponeCommandHandler_AcceptedPostponedTemplate,
                            Tuple.Create(
                                LocTokens.Users,
                                _configuration.PostponeQuorum.ToString())));
                    break;
                case PostponeService.PostponeResult.DeniedNoQuotaLeft:
                    await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                        LocTokens.SubstituteTokens(_loc.PostponeCommandHandler_DeniedNoQuotaLeftTemplate,
                            Tuple.Create(
                                LocTokens.Time,
                                _configuration.PostponeHoursAllowed.ToString("0."))));
                    break;
                case PostponeService.PostponeResult.DeniedAlreadyHasOpen:
                    await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                        LocTokens.SubstituteTokens(_loc.PostponeCommandHandler_DeniedAlreadyHasOpenTemplate,
                            Tuple.Create(
                                LocTokens.Users,
                                _configuration.PostponeQuorum.ToString())));
                    break;
                case PostponeService.PostponeResult.GeneralFailure:
                    await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                        _loc.PostponeCommandHandler_GeneralFailure);
                    break;
                default:
                    logger.Error($"Unhandled case - {result}");
                    await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                        _loc.PostponeCommandHandler_GeneralFailure);
                    break;
            }


            logger.Info($"Postpone request from {user.GetUsernameOrNameWithCircumflex()} resulted in {result}");
        }

    }
}