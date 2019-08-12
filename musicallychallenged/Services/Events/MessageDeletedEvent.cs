using Telegram.Bot.Types;

namespace musicallychallenged.Services.Events
{
    public class MessageDeletedEvent : IAggregateMessage
    {
        public ChatId ChatId { get; }
        public int? MessageId { get; }

        public MessageDeletedEvent(ChatId chatId, int? messageId)
        {
            ChatId = chatId;
            MessageId = messageId;
        }
    }
}