using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace tests.Mockups.Messaging
{
    internal class MessageSentMock : MockMessage
    {
        public ChatId ChatId { get; }
        public int Id { get; }
        public string Text { get; }
        public ParseMode ParseMode { get; }
        public bool DisableWebPagePreview { get; }
        public bool DisableNotification { get; }
        public int ReplyToMessageId { get; }
        public IReplyMarkup ReplyMarkup { get; }

        public MessageSentMock(ChatId chatId, int id, string text, ParseMode parseMode, bool disableWebPagePreview,
            bool disableNotification, int replyToMessageId, IReplyMarkup replyMarkup)
        {
            ChatId = chatId;
            Id = id;
            Text = text;
            ParseMode = parseMode;
            DisableWebPagePreview = disableWebPagePreview;
            DisableNotification = disableNotification;
            ReplyToMessageId = replyToMessageId;
            ReplyMarkup = replyMarkup;
        }

        public override string ToString()
        {
            return $"MessageSentMock: {Id} -> {ChatId.Identifier}: {Text}";
        }
    }
}