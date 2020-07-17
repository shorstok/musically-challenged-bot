using Telegram.Bot.Types;

namespace tests.Mockups.Messaging
{
    internal class MessagePinnedMock : MockMessage
    {
        public ChatId ChatId { get; }
        public int MessageId { get; }
        public bool DisableNotification { get; }

        public MessagePinnedMock(ChatId chatId, int messageId, bool disableNotification)
        {
            ChatId = chatId;
            MessageId = messageId;
            DisableNotification = disableNotification;
        }
    }
}