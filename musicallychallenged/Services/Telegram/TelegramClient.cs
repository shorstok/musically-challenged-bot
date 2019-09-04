using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Logging;
using musicallychallenged.Services.Events;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace musicallychallenged.Services.Telegram
{
    //todo: catch 'blocked', 'chat not found' exceptions and post events to EventQueue and handle them elsewhere (delete chat references from db, signal to admins...)
    public class TelegramClient : ITelegramClient
    {
        private static readonly ILog logger = Log.Get(typeof(TelegramClient));

        private readonly TelegramBotClient _client;
        private readonly BotConfiguration _configuration;
        private readonly IEventAggregator _eventAggregator;
        private readonly SemaphoreSlim _messageSemaphoreSlim = new SemaphoreSlim(1, 1);

        private readonly ConcurrentBag<DateTime> _messageSendTimes = new ConcurrentBag<DateTime>();

        private DateTime? _lastSendDateTime;


        public TelegramClient(BotConfiguration configuration, IEventAggregator eventAggregator)
        {
            _configuration = configuration;
            _eventAggregator = eventAggregator;

            _client = new TelegramBotClient(_configuration.TelegramAnnouncerBotKey.Unprotect());
        }

        public async Task ConnectAsync()
        {
            var ts = Stopwatch.StartNew();
            var bSignaled = false;

            while (!await ConnectivityService.CheckIsConnected() && ts.Elapsed.TotalMinutes < 10)
            {
                if (!bSignaled)
                    logger.Warn($"No connection, waiting for connection restore before setting up telegram bot");

                bSignaled = true;
            }

            if (bSignaled)
                logger.Info(
                    $"Wait ended, connection {(await ConnectivityService.CheckIsConnected() ? "restored" : "absent")}");

            _client.StartReceiving();
        }


        public void StopReceiving()
        {
            _client.StopReceiving();
        }

        public async Task DeleteMessageAsync(ChatId chatId, int messageId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await ExecuteThrottled(
                () => _client.DeleteMessageAsync(chatId, messageId, cancellationToken).ContinueWith(s => 0),
                cancellationToken, 
                FaultSource.MessageInChat(chatId,messageId));
        }

        public async Task<Message> EditMessageTextAsync(ChatId chatId, int messageId, string text,
            ParseMode parseMode = ParseMode.Default, bool disableWebPagePreview = false,
            InlineKeyboardMarkup replyMarkup = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await ExecuteThrottled(() => _client.EditMessageTextAsync(chatId, messageId, text, parseMode,
                    disableWebPagePreview, replyMarkup,
                    cancellationToken),
                cancellationToken,
                FaultSource.MessageInChat(chatId,messageId));
        }

        public async Task AnswerCallbackQueryAsync(string callbackQueryId, string text = null, bool showAlert = false,
            string url = null,
            int cacheTime = 0, CancellationToken cancellationToken = default(CancellationToken))
        {
            await ExecuteThrottled(
                () => _client
                    .AnswerCallbackQueryAsync(callbackQueryId, text, showAlert, url, cacheTime, cancellationToken)
                    .ContinueWith(s => 0), cancellationToken,
                FaultSource.CallbackQuery(callbackQueryId));
        }

        public async Task<Message> ForwardMessageAsync(ChatId chatId, ChatId fromChatId, int messageId,
            bool disableNotification = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await ExecuteThrottled(
                () => _client.ForwardMessageAsync(chatId, fromChatId, messageId, disableNotification,
                    cancellationToken), cancellationToken,
                FaultSource.ForwardedMessageInChat(fromChatId,chatId,messageId));
        }


        public async Task<Message> EditMessageReplyMarkupAsync(ChatId chatId, int messageId,
            InlineKeyboardMarkup replyMarkup = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            //Guard EditMessageReplyMarkupAsync: it's ok to silently eat exceptions on markup edit failure
            return await ExecuteThrottled(
                () => _client.EditMessageReplyMarkupAsync(chatId, messageId, replyMarkup, cancellationToken),
                cancellationToken,
                FaultSource.MessageInChat(chatId,messageId));
        }

        public async Task PinChatMessageAsync(ChatId chatId, int messageId, bool disableNotification = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                //Guard PinChatMessageAsync: it's ok to silently eat exceptions on markup edit failure
                await ExecuteThrottled(
                    () => _client.
                        PinChatMessageAsync(chatId, messageId, disableNotification, cancellationToken).
                        ContinueWith(t => 0), 
                    cancellationToken,
                    FaultSource.MessageInChat(chatId,messageId));
            }
            catch (Exception e)
            {
                logger.Error($"Telegram bot error when pinning message", e);
            }
        }

        public async Task<Message> SendTextMessageAsync(ChatId chatId, string text,
            ParseMode parseMode = ParseMode.Default,
            bool disableWebPagePreview = false, bool disableNotification = false, int replyToMessageId = 0,
            IReplyMarkup replyMarkup = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await ExecuteThrottled(() => _client.SendTextMessageAsync(chatId, text, parseMode,
                disableWebPagePreview,
                disableNotification,
                replyToMessageId, replyMarkup, cancellationToken), cancellationToken, FaultSource.ForChat(chatId));
        }

        private struct FaultSource
        {
            public ChatId SourceChat;
            public ChatId TargetChat;
            public int? MessageId;
            public string CallbackQueryId;

            private FaultSource(ChatId sourceChat, ChatId targetChat, int? messageId, string callbackQueryId)
            {
                SourceChat = sourceChat;
                TargetChat = targetChat;
                MessageId = messageId;
                CallbackQueryId = callbackQueryId;
            }

            public static FaultSource ForChat(ChatId chat) => new FaultSource(chat,null,null,null);
            public static FaultSource MessageInChat(ChatId chat, int message) => new FaultSource(chat,null,message,null);
            public static FaultSource ForwardedMessageInChat(ChatId src, ChatId dest, int message) => new FaultSource(src,dest,message,null);
            public static FaultSource CallbackQuery(string id) => new FaultSource(null,null,null,id);
        }

        public event EventHandler<MessageEventArgs> OnMessage
        {
            add => _client.OnMessage += value;
            remove => _client.OnMessage -= value;
        }

        public event EventHandler<UpdateEventArgs> OnUpdate
        {
            add => _client.OnUpdate += value;
            remove => _client.OnUpdate -= value;
        }

        public event EventHandler<MessageEventArgs> OnMessageEdited
        {
            add => _client.OnMessageEdited += value;
            remove => _client.OnMessageEdited -= value;
        }

        public event EventHandler<CallbackQueryEventArgs> OnCallbackQuery
        {
            add => _client.OnCallbackQuery += value;
            remove => _client.OnCallbackQuery -= value;
        }

        public event EventHandler<InlineQueryEventArgs> OnInlineQuery
        {
            add => _client.OnInlineQuery += value;
            remove => _client.OnInlineQuery -= value;
        }

        public event EventHandler<ChosenInlineResultEventArgs> OnInlineResultChosen
        {
            add => _client.OnInlineResultChosen += value;
            remove => _client.OnInlineResultChosen -= value;
        }

        public event EventHandler<ReceiveErrorEventArgs> OnReceiveError
        {
            add => _client.OnReceiveError += value;
            remove => _client.OnReceiveError -= value;
        }

        private async Task<T> ExecuteThrottled<T>(Func<Task<T>> task, CancellationToken cancellationToken,
            FaultSource identifiers)
        {
            const int guardExtraDelayMs = 500;

            //Telegram is subject to blocking on burst sends
            if (_lastSendDateTime.HasValue)
            {
                var delta = (int) (DateTime.UtcNow - _lastSendDateTime.Value).TotalMilliseconds;

                if (delta < 50)
                    await Task.Delay(50 - delta, cancellationToken).ConfigureAwait(false);
            }

            await _messageSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var sw = Stopwatch.StartNew();

                //If somebody tried to interact before bot started

                while (_client == null && sw.Elapsed.TotalSeconds < 5)
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                if (_client == null)
                {
                    logger.Warn($"Time-out waiting for tg bot to start, message dropped");
                    return default(T);
                }

                //Tg bots are subject to message throttle (regardless sending to one user or multiple), 
                //and we have to keep track of minimum time delay by ourselves

                var lastSecondCount = 0;
                var timeWindowStart = DateTime.Now.AddSeconds(-1);
                var lastMessageTimesOrdered =
                    _messageSendTimes.Where(time => time >= timeWindowStart).OrderBy(t => t).ToArray();

                lastSecondCount = lastMessageTimesOrdered.Length;

                if (lastSecondCount >= _configuration.TelegramMaxMessagesPerSecond)
                {
                    var skipQty = lastSecondCount - _configuration.TelegramMaxMessagesPerSecond;

                    var mostOffendingMessage = lastMessageTimesOrdered.Skip(skipQty).FirstOrDefault();

                    //How much to wait till most offending message leaves time window
                    var clearance = (mostOffendingMessage - timeWindowStart).TotalMilliseconds +
                                    guardExtraDelayMs;

                    await Task.Delay((int) clearance, cancellationToken).ConfigureAwait(false);
                }

                _messageSendTimes.Add(DateTime.Now);

                var retval = await task().ConfigureAwait(false);

                _lastSendDateTime = DateTime.UtcNow;

                return retval;
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                logger.Error($"Got HttpRequestException {e.Message}");
                
                await HandleInternetConnectionLostAysnc();

                return default(T);
            }
            catch (MessageIsNotModifiedException)
            {
                logger.Warn($"Telegram squeak about MessageIsNotModified");
                return default(T);
            }
            catch (ApiRequestException apiRequestException)
            {
                logger.Warn($"ApiRequestException: {apiRequestException.ErrorCode} {apiRequestException.Message}");

                //Why Telegram didnt implement any codes for errors? What if error text changes? :/
                if (apiRequestException.Message?.Contains("message to edit not found") == true)
                    _eventAggregator.Publish(new MessageDeletedEvent(identifiers.SourceChat, identifiers.MessageId));
                if(apiRequestException.ErrorCode == 403 && apiRequestException.Message?.Contains("blocked by the user") == true)
                    _eventAggregator.Publish(new BotBlockedEvent(identifiers.SourceChat, identifiers.MessageId));

                return default(T);
            }
            catch (Exception e)
            {
                logger.Error($"Telegram bot error", e);

                return default(T);
            }
            finally
            {
                _messageSemaphoreSlim.Release();

                if (_messageSendTimes.Count > _configuration.TelegramMaxMessagesPerSecond)
                    _messageSendTimes.TryTake(out var _);
            }
        }

        private async Task HandleInternetConnectionLostAysnc()
        {
            bool connectionLossDetected = false;

            while (!await ConnectivityService.CheckIsConnected())
            {
                if (!connectionLossDetected)
                {
                    logger.Info($"Detected internet connection lost, halting bot for good");
                    connectionLossDetected = true;
                }

                await Task.Delay(15000).ConfigureAwait(false);
            }

            if(connectionLossDetected)
                logger.Info($"Connection seems to be restored");
        }
    }
}