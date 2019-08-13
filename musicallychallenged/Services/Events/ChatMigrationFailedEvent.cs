using Telegram.Bot.Types;

namespace musicallychallenged.Services.Events
{
    public class ChatMigrationFailedEvent : IAggregateMessage
    {
        public Message MigrationMessage { get; }

        public ChatMigrationFailedEvent(Message migrationMessage)
        {
            MigrationMessage = migrationMessage;
        }
    }
}