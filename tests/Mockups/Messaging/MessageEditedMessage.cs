using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace tests.Mockups.Messaging
{
    internal class MessageEditedMock : MockMessage
    {
        public ChatId ChatId { get; }
        public int MessageId { get; }
        public string Text { get; }
        public ParseMode ParseMode { get; }
        public bool DisableWebPagePreview { get; }
        public InlineKeyboardMarkup ReplyMarkup { get; }

        public MessageEditedMock(ChatId chatId, int messageId, string text, ParseMode parseMode,
            bool disableWebPagePreview, InlineKeyboardMarkup replyMarkup)
        {
            ChatId = chatId;
            MessageId = messageId;
            Text = text;
            ParseMode = parseMode;
            DisableWebPagePreview = disableWebPagePreview;
            ReplyMarkup = replyMarkup;
        }
    }
}