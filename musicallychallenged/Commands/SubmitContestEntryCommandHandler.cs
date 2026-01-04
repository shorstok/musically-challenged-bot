using System;
using System.ComponentModel;
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
        private readonly IBotConfiguration _configuration;
        private readonly ContestController _contestController;
        private readonly MidvoteEntryController _midvoteEntryController;
        private readonly LocStrings _loc;

        public string CommandName { get; } = Schema.SubmitCommandName;
        public string UserFriendlyDescription => _loc.SubmitContestEntryCommandHandler_Description;

        private static readonly ILog logger = Log.Get(typeof(SubmitContestEntryCommandHandler));

        public SubmitContestEntryCommandHandler(IRepository repository, 
            IBotConfiguration configuration,
            ContestController contestController,
            MidvoteEntryController midvoteEntryController,
            LocStrings loc)
        {
            _repository = repository;
            _configuration = configuration;
            _contestController = contestController;
            _midvoteEntryController = midvoteEntryController;
            _loc = loc;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            var state = _repository.GetOrCreateCurrentState();
            bool isMidvoteSubmission = false;

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to submit contest entry");

            if (state.State != ContestState.Contest)
            {
                logger.Info($"Bot in non-contest state {state.State}");
                
                if (await _midvoteEntryController.IsAvailable(user))
                {
                    await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                        _loc.SubmitContestEntryCommandHandler_ProvideMidvotePin);
                    
                    var pin =  await dialog.GetMessageInThreadAsync(
                        new CancellationTokenSource(TimeSpan.FromMinutes(_configuration.SubmissionTimeoutMinutes)).Token);

                    if (!await _midvoteEntryController.ValidatePin(pin))
                    {
                        logger.Info($"Midvote pin validation failed");
                        await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                            _loc.SubmitContestEntryCommandHandler_InvalidMidvotePin);
                        
                        return;
                    }
                    
                    isMidvoteSubmission = true;
                }
                else
                {
                    logger.Info($"Submission denied");
                    await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, _loc.SubmitContestEntryCommandHandler_OnlyAvailableInContestState);
                    return;                    
                }

            }

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, _loc.SubmitContestEntryCommandHandler_SubmitGuidelines);

            var response = await dialog.GetMessageInThreadAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(_configuration.SubmissionTimeoutMinutes)).Token);

            var (isValid, error) = ValidateContestMessage(response);
            
            if(!isValid)
            {
                error ??= _loc.SubmitContestEntryCommandHandler_SubmissionFailed;
                
                logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} failed sumbission validation: {error}");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, error);
                return;
            }

            if (isMidvoteSubmission)
                await _midvoteEntryController.SubmitNewEntry(response, user);
            else
                await _contestController.SubmitNewEntry(response, user);

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, _loc.SubmitContestEntryCommandHandler_SubmissionSucceeded);

            logger.Info($"Contest entry submitted");
        }

        private (bool isValid, string customError) ValidateContestMessage(Message message)
        {
            //Because pesnocloud, we accept only audio entries 
            if(message.Audio == null)
                return (isValid: false, customError: _loc.SubmitContestEntryCommandHandler_SubmissionFailedNoAudio);
            
            if (message.Audio?.FileSize >= 20_000_000)
                return (isValid: false, customError: _loc.SubmitContestEntryCommandHandler_SubmissionFailedTooLarge);

            return (isValid: true, customError: null);
        }

    }
}