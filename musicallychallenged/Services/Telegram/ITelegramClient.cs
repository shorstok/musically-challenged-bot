using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace musicallychallenged.Services.Telegram
{
    public interface ITelegramClient
    {
        Task ConnectAsync();
        void StopReceiving();


        Task<Message> SendTextMessageAsync(ChatId chatId, string text, ParseMode parseMode = ParseMode.Default,
            bool disableWebPagePreview = false, bool disableNotification = false, int replyToMessageId = 0,
            IReplyMarkup replyMarkup = null, CancellationToken cancellationToken = default(CancellationToken));

        Task PinChatMessageAsync(ChatId chatId, int messageId, bool disableNotification = false,
            CancellationToken cancellationToken = default(CancellationToken));

        Task AnswerCallbackQueryAsync(string callbackQueryId, string text = null, bool showAlert = false, string url = null, int cacheTime = 0, CancellationToken cancellationToken = default(CancellationToken));

        Task<Message> EditMessageReplyMarkupAsync(ChatId chatId, int messageId, InlineKeyboardMarkup replyMarkup = null, CancellationToken cancellationToken = default(CancellationToken));

        Task<Message> ForwardMessageAsync(ChatId chatId, ChatId fromChatId, int messageId, bool disableNotification = false, CancellationToken cancellationToken = default(CancellationToken));

        Task DeleteMessageAsync(ChatId chatId, int messageId,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<Message> EditMessageTextAsync(ChatId chatId, int messageId, string text,
            ParseMode parseMode = ParseMode.Default, bool disableWebPagePreview = false,
            InlineKeyboardMarkup replyMarkup = null,
            CancellationToken cancellationToken = default(CancellationToken));

        event EventHandler<UpdateEventArgs> OnUpdate;
        event EventHandler<MessageEventArgs> OnMessage;
        event EventHandler<MessageEventArgs> OnMessageEdited;
        event EventHandler<CallbackQueryEventArgs> OnCallbackQuery;
        event EventHandler<InlineQueryEventArgs> OnInlineQuery;
        event EventHandler<ChosenInlineResultEventArgs> OnInlineResultChosen;
        event EventHandler<ReceiveErrorEventArgs> OnReceiveError;
        
    }
}