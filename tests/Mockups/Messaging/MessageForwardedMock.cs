using Telegram.Bot.Types;

namespace tests.Mockups.Messaging
{
    internal class MessageForwardedMock : MockMessage
    {
        public ChatId FromChatId { get; }
        public ChatId ChatId { get; }
        public int MessageId { get; }
        public bool DisableNotification { get; }

        public MessageForwardedMock(ChatId fromChatId, ChatId chatId, int messageId, bool disableNotification)
        {
            FromChatId = fromChatId;
            ChatId = chatId;
            MessageId = messageId;
            DisableNotification = disableNotification;
        }
    }
}