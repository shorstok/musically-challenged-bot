using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using NUnit.Framework;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using tests.DI;
using tests.Mockups.Messaging;

namespace tests.Mockups
{
    public class MockTelegramClient : ITelegramClient
    {
        private readonly UserScenarioController _userScenarioController;
        private readonly MockMessageMediatorService _mediatorService;
        private static readonly ILog Logger = Log.Get(typeof(MockTelegramClient));

        private readonly ConcurrentDictionary<Tuple<long, int>, Message> _mockMessages =
            new ConcurrentDictionary<Tuple<long, int>, Message>();

        private readonly ConcurrentDictionary<string, int> _userIdForPendingCallbackQueries =
            new ConcurrentDictionary<string, int>();


        public MockTelegramClient(UserScenarioController userScenarioController,
            MockMessageMediatorService mediatorService)
        {
            _userScenarioController = userScenarioController;
            _mediatorService = mediatorService;

            _mediatorService.OnInsertMockMessage += _mediatorService_OnInsertMockMessage;
            _mediatorService.OnGetMockMessage += _mediatorService_OnGetMockMessage;
        }

        private void _mediatorService_OnGetMockMessage(long chatId, int messageId, out Message message)
        {
            _mockMessages.TryGetValue(Tuple.Create<long, int>(chatId, messageId), out message);
        }

        private void _mediatorService_OnInsertMockMessage(Message message)
        {
            _mockMessages.AddOrUpdate(Tuple.Create(message.Chat.Id, message.MessageId), message,
                (tuple, existing) => message);
        }

        public Task ConnectAsync()
        {
            Logger.Info("ConnectAsync / recreating mock user message queue");

            return Task.FromResult(true);
        }

        public void StopReceiving()
        {
            Logger.Info("StopReceiving / finalizing mock user message queue");
        }

        public async Task AnswerCallbackQueryAsync(string callbackQueryId,
            string text = null,
            bool showAlert = false,
            string url = null,
            int cacheTime = 0, CancellationToken cancellationToken = default)
        {
            if (!_userIdForPendingCallbackQueries.TryRemove(callbackQueryId, out var target))
                throw new Exception(
                    $"Answer callback query {callbackQueryId} -- callback query with such id is not registered");

            await _userScenarioController.SendMessageToMockUser(target,
                new AnswerCallbackQueryMock(callbackQueryId, text, showAlert, url, cacheTime), cancellationToken);
        }

        public async Task<Message> EditMessageTextAsync(ChatId chatId, int messageId, string text,
            ParseMode parseMode = ParseMode.Default,
            bool disableWebPagePreview = false, InlineKeyboardMarkup replyMarkup = null,
            CancellationToken cancellationToken = default)
        {
            MaybeAssertValidHTML(parseMode, text);

            await _userScenarioController.SendMessageToMockUsers(chatId,
                new MessageEditedMock(chatId, messageId, text, parseMode, disableWebPagePreview, replyMarkup),
                cancellationToken);

            if (_mockMessages.TryGetValue(new Tuple<long, int>(chatId.Identifier, messageId), out var existing))
            {
                existing.Text = text;
                existing.ReplyMarkup = replyMarkup;
                return existing;
            }

            return null;
        }

        public async Task DeleteMessageAsync(ChatId chatId, int messageId,
            CancellationToken cancellationToken = default)
        {
            _mockMessages.TryRemove(Tuple.Create(chatId.Identifier, messageId), out _);

            await _userScenarioController.SendMessageToMockUsers(chatId, new MessageDeletedMock(chatId, messageId),
                cancellationToken);
        }

        public async Task<Message> ForwardMessageAsync(ChatId chatId, ChatId fromChatId, int messageId,
            bool disableNotification = false,
            CancellationToken cancellationToken = default)
        {
            if (!_mockMessages.TryGetValue(Tuple.Create(fromChatId.Identifier, messageId), out var result))
                return null;

            result.Chat = new Chat
            {
                Id = chatId.Identifier
            };

            _mockMessages.AddOrUpdate(Tuple.Create(chatId.Identifier, messageId), result,
                (tuple, existing) => result);


            await _userScenarioController.SendMessageToMockUsers(chatId,
                new MessageForwardedMock(fromChatId, chatId, messageId, disableNotification),
                cancellationToken);

            return result;
        }

        public async Task<Message> EditMessageReplyMarkupAsync(ChatId chatId, int messageId,
            InlineKeyboardMarkup replyMarkup = null,
            CancellationToken cancellationToken = default)
        {
            await _userScenarioController.SendMessageToMockUsers(chatId,
                new MessageMarkupEditedMock(chatId, messageId, replyMarkup),
                cancellationToken);

            if (_mockMessages.TryGetValue(new Tuple<long, int>(chatId.Identifier, messageId), out var existing))
            {
                existing.ReplyMarkup = replyMarkup;
                return existing;
            }

            return null;
        }

        public async Task PinChatMessageAsync(ChatId chatId, int messageId, bool disableNotification = false,
            CancellationToken cancellationToken = default)
        {
            await _userScenarioController.SendMessageToMockUsers(chatId,
                new MessagePinnedMock(chatId, messageId, disableNotification),
                cancellationToken);
        }

        public async Task<Message> SendTextMessageAsync(ChatId chatId, string text,
            ParseMode parseMode = ParseMode.Default,
            bool disableWebPagePreview = false, bool disableNotification = false, int replyToMessageId = 0,
            IReplyMarkup replyMarkup = null, CancellationToken cancellationToken = default)
        {
            var id = MockConfiguration.CreateNewMockMessageId();
            Message replyMessage = null;

            MaybeAssertValidHTML(parseMode, text);

            if (replyToMessageId != 0)
                _mockMessages.TryGetValue(Tuple.Create(chatId.Identifier, replyToMessageId), out replyMessage);

            var message = new Message
            {
                Chat = new Chat {Id = chatId.Identifier},
                MessageId = id,
                Text = text,
                ReplyToMessage = replyMessage
            };

            _mockMessages.AddOrUpdate(Tuple.Create(chatId.Identifier, id), message, (tuple, existing) => message);

            await _userScenarioController.SendMessageToMockUsers(chatId,
                new MessageSentMock(chatId, id, text, parseMode, disableWebPagePreview, disableNotification,
                    replyToMessageId, replyMarkup),
                cancellationToken);

            return message;
        }

        public event EventHandler<UpdateEventArgs> OnUpdate;
        public event EventHandler<MessageEventArgs> OnMessage;
        public event EventHandler<MessageEventArgs> OnMessageEdited;
        public event EventHandler<CallbackQueryEventArgs> OnCallbackQuery;
        public event EventHandler<InlineQueryEventArgs> OnInlineQuery;
        public event EventHandler<ChosenInlineResultEventArgs> OnInlineResultChosen;
        public event EventHandler<ReceiveErrorEventArgs> OnReceiveError;

        internal virtual void InvokeOnUpdate(UpdateEventArgs e)
        {
            OnUpdate?.Invoke(this, e);
        }

        internal virtual void InvokeOnMessage(MessageEventArgs e)
        {
            _mockMessages.TryAdd(Tuple.Create(e.Message.Chat.Id, e.Message.MessageId), e.Message);

            OnMessage?.Invoke(this, e);
        }

        internal virtual void InvokeOnMessageEdited(MessageEventArgs e)
        {
            OnMessageEdited?.Invoke(this, e);
        }

        internal virtual void InvokeOnCallbackQuery(CallbackQueryEventArgs e)
        {
            _userIdForPendingCallbackQueries.AddOrUpdate(e.CallbackQuery.Id, e.CallbackQuery.From.Id,
                (s, existing) => e.CallbackQuery.From.Id);

            OnCallbackQuery?.Invoke(this, e);
        }

        public Message GetMockMessageById(long chatIdIdentifier, int messageSentMessageId)
        {
            return !_mockMessages.TryGetValue(Tuple.Create(chatIdIdentifier, messageSentMessageId), out var message)
                ? null
                : message;
        }


        private void MaybeAssertValidHTML(ParseMode parseMode, string text)
        {
            if (parseMode == ParseMode.Default)
                return;

            if (parseMode == ParseMode.Html)
            {
                LocalizationTestingHelper.AssertValidTelegramHtml(text);
            }

            if (parseMode == ParseMode.Markdown)
                Assert.Fail("We're not using Markdown in this bot, use HTML plz");
        }
    }
}