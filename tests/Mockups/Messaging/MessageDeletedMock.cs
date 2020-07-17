using Telegram.Bot.Types;

namespace tests.Mockups.Messaging
{
    internal class MessageDeletedMock : MockMessage
    {
        public ChatId ChatId { get; }
        public int MessageId { get; }

        public MessageDeletedMock(ChatId chatId, int messageId)
        {
            ChatId = chatId;
            MessageId = messageId;
        }
    }
}