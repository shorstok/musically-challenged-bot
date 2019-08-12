using Telegram.Bot.Types;

namespace musicallychallenged.Services.Events
{
    public class BotBlockedEvent : IAggregateMessage
    {
        public ChatId ChatId { get; }
        public int? MessageId { get; }

        public BotBlockedEvent(ChatId chatId, int? messageId)
        {
            ChatId = chatId;
            MessageId = messageId;
        }
    }
}