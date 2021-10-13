using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Domain;
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
        private readonly IBotConfiguration _configuration;
        private static readonly ILog logger = Log.Get(typeof(NewTaskSelectorController));

        public const string RandomTaskCallbackId = "rnd";
        public const string NextRoundTaskPollCallbackId = "nrtp";

        public NewTaskSelectorController(DialogManager dialogManager,
            ITelegramClient client,
            LocStrings loc, IBotConfiguration configuration)
        {
            _dialogManager = dialogManager;
            _client = client;
            _loc = loc;
            _configuration = configuration;

            _replyMessageForSelectedTaskKind = new Dictionary<SelectedTaskKind, string>
            {
                {SelectedTaskKind.Random,  _loc.RandomTaskSelectedMessage},
                {SelectedTaskKind.Manual, _loc.TaskSelectedMessage },
                {SelectedTaskKind.Poll, _loc.InitiatedNextRoundTaskPollMessage },
            };
        }

        public async Task<Tuple<SelectedTaskKind, string>> SelectTaskAsync(User winner)
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

            return Tuple.Create(SelectedTaskKind.Poll, string.Empty);
        }

        private async Task<Tuple<SelectedTaskKind, string>> SelectTaskAsyncInternal(User winner)
        {
            Tuple<SelectedTaskKind, string> proposedTask = null;

            using (var dialog = _dialogManager.StartNewDialogExclusive(winner.ChatId.Value, winner.Id, "SelectTaskAsyncInternal"))
            {
                //Create message with 'random' button

                var message = await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                    _loc.ChooseNextRoundTaskPrivateMessage, ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(_loc.RandomTaskButtonLabel, RandomTaskCallbackId),
                        InlineKeyboardButton.WithCallbackData(_loc.NextRoundTaskPollButtonLabel, NextRoundTaskPollCallbackId),
                    }));

                if (null == message)
                {
                    logger.Warn($"For some reason couldnt start task selection sequence with {winner.GetUsernameOrNameWithCircumflex()}, " +
                                $"falling back to 'poll' choice");

                    return Tuple.Create(SelectedTaskKind.Poll, string.Empty);
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
                                    proposedTask = ParseCallbackQuery(query, token).Result;
                                    break;

                                case Message contestMessage:
                                    if (String.IsNullOrWhiteSpace(contestMessage.Text) || contestMessage.Text?.Length < 5)
                                    {
                                        logger.Warn($"User sent {proposedTask} as task for new round, declined");

                                        await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                                            _loc.InvalidTaskMessage,
                                            ParseMode.Html,
                                            cancellationToken: CancellationToken.None);
                                    }
                                    else
                                    {
                                        proposedTask = Tuple.Create(SelectedTaskKind.Manual, contestMessage.Text);
                                        logger.Info($"User chosen task {proposedTask.Item2}");
                                    }
                                    break;
                            }
                        } while (proposedTask == null);
                    }
                    catch (Exception)
                    {
                        proposedTask = Tuple.Create(SelectedTaskKind.Poll, string.Empty);
                    }

                    cancellationTokenSource.Cancel();
                }

                //Remove reply buttons
                await _client.EditMessageReplyMarkupAsync(message.Chat.Id, message.MessageId,
                    replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove

                await _client.SendTextMessageAsync(dialog.ChatId,
                    _replyMessageForSelectedTaskKind[proposedTask.Item1],
                    ParseMode.Html,
                    cancellationToken: CancellationToken.None);

                _dialogManager.RecycleDialog(dialog);
            }

            return proposedTask;
        }

        private readonly Dictionary<SelectedTaskKind, string> _replyMessageForSelectedTaskKind;

        private async Task<Tuple<SelectedTaskKind, string>> ParseCallbackQuery(CallbackQuery query, CancellationToken token)
        {
            await _client.AnswerCallbackQueryAsync(query.Id, cancellationToken: token);

            switch (query.Data)
            {
                case RandomTaskCallbackId:
                    logger.Info($"User opted for random task");
                    return Tuple.Create(SelectedTaskKind.Random, string.Empty);

                case NextRoundTaskPollCallbackId:
                    logger.Info($"User opted for the NextRoundTaskPoll");
                    return Tuple.Create(SelectedTaskKind.Poll, string.Empty);
            }

            logger.Error($"Wtf, got callback data = {query.Data}, falling back to 'poll'");

            return Tuple.Create(SelectedTaskKind.Poll, string.Empty);
        }
    }
}
