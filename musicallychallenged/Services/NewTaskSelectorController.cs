using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Helpers;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public class NewTaskSelectorController
    {
        private readonly DialogManager _dialogManager;
        private readonly ITelegramClient _client;
        private readonly LocStrings _loc;
        private readonly BotConfiguration _configuration;
        private static readonly ILog logger = Log.Get(typeof(NewTaskSelectorController));

        public const string RandomTaskCallbackId = "rnd";

        public NewTaskSelectorController(DialogManager dialogManager,
            ITelegramClient client,
            LocStrings loc, BotConfiguration configuration)
        {
            _dialogManager = dialogManager;
            _client = client;
            _loc = loc;
            _configuration = configuration;
        }

        public async Task<string> SelectTaskAsync(User winner)
        {
            try
            {
                return await SelectTaskAsyncInternal(winner);
            }
            catch (ApiRequestException  e)
            {
                logger.Warn($"Got ApiRequestException exception {e.Message} upon task selection");
            }
            catch (Exception e)
            {
                logger.Warn($"Got unexpected exception {e.Message}");
            }

            return RandomTaskCallbackId;
        }

        private async Task<string> SelectTaskAsyncInternal(User winner)
        {
            string proposedTask = null;

            using (var dialog = _dialogManager.StartNewDialogExclusive(winner.ChatId.Value, winner.Id, "SelectTaskAsyncInternal"))
            {
                //Create message with 'random' button

                var message = await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                    _loc.ChooseNextRoundTaskPrivateMessage, ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(_loc.RandomTaskButtonLabel, RandomTaskCallbackId),
                    }));

                if (null == message)
                {
                    logger.Warn($"For some reason couldnt start task selection sequence with {winner.GetUsernameOrNameWithCircumflex()}, " +
                                $"falling back to 'random' choice");

                    return RandomTaskCallbackId;
                }

                using (var cancellationTokenSource =
                    new CancellationTokenSource(TimeSpan.FromHours(_configuration.MaxTaskSelectionTimeHours)))
                {
                    //Asynchronously fire task that would signal user after 10 minutes that timeout is present

                    var chatId = dialog.ChatId;

                    var t = Task.Delay(TimeSpan.FromMinutes(10), cancellationTokenSource.Token).ContinueWith(async task =>
                    {
                        await _client.SendTextMessageAsync(chatId,
                            LocTokens.SubstituteTokens(_loc.SlackWarningMesage,
                                Tuple.Create(LocTokens.Time,
                                    _configuration.MaxTaskSelectionTimeHours.ToString(CultureInfo.InvariantCulture))));
                    }, cancellationTokenSource.Token).ConfigureAwait(false);

                    try
                    {
                        do
                        {
                            var token = cancellationTokenSource.Token;

                            //Task.WhenAny workaround 
                            //(boxing because GetMessageInThreadAsync and GetCallbackQueryAsync tasks return different types)

                            var response = await Task.WhenAny(
                                    TaskEx.TaskToObject(dialog.GetMessageInThreadAsync(token)),
                                    TaskEx.TaskToObject(dialog.GetCallbackQueryAsync(token)))
                                .Unwrap();

                            switch (response)
                            {
                                case CallbackQuery query:
                                    
                                    await _client.AnswerCallbackQueryAsync(query.Id,cancellationToken:token);
                                    
                                    if (query.Data != RandomTaskCallbackId)
                                        logger.Error(
                                            $"Wtf, got callback data = {query.Data}, expected {RandomTaskCallbackId} but anyway");

                                    proposedTask = RandomTaskCallbackId;

                                    logger.Info($"User opted for random task");

                                    break;

                                case Message contestMessage:

                                    proposedTask = contestMessage.Text;

                                    if (String.IsNullOrWhiteSpace(proposedTask) || proposedTask?.Length < 5)
                                    {
                                        logger.Warn($"User sent {proposedTask} as task for new round, declined");

                                        await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                                            _loc.InvalidTaskMessage,
                                            ParseMode.Html,
                                            cancellationToken: CancellationToken.None);

                                        proposedTask = null;
                                    }
                                    else
                                        logger.Info($"User chosen task {proposedTask}");

                                    break;

                            }
                        } while (proposedTask == null);
                    }
                    catch (Exception)
                    {
                        proposedTask = RandomTaskCallbackId;
                    }

                    cancellationTokenSource.Cancel();
                }

                //Remove 'random' button
                await _client.EditMessageReplyMarkupAsync(message.Chat.Id, message.MessageId,
                    replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove

                await _client.SendTextMessageAsync(dialog.ChatId,
                    proposedTask == RandomTaskCallbackId ?
                        _loc.RandomTaskSelectedMessage :
                        _loc.TaskSelectedMessage,
                    ParseMode.Html,
                    cancellationToken: CancellationToken.None);

                _dialogManager.RecycleDialog(dialog);
            }

            return proposedTask;
        }
    }
}
