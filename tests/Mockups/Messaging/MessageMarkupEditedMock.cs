using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace tests.Mockups.Messaging
{
    internal class MessageMarkupEditedMock : MockMessage
    {
        public ChatId ChatId { get; }
        public int MessageId { get; }
        public InlineKeyboardMarkup ReplyMarkup { get; }

        public MessageMarkupEditedMock(ChatId chatId, int messageId, InlineKeyboardMarkup replyMarkup)
        {
            ChatId = chatId;
            MessageId = messageId;
            ReplyMarkup = replyMarkup;
        }
    }
}