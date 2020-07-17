using System.Threading;
using musicallychallenged.Config;
using Telegram.Bot.Types;

namespace tests.Mockups
{
    public static class MockConfiguration
    {
        private static int _mockMessageIdAutoincrement = 1;
        private static int _mockUserIdAutoincrement = 1;
        private static long _mockUserPrivateChatsId = 3;

        public static Chat MainChat { get; } = new Chat {Id = 1};
        public static Chat VotingChat { get; } = new Chat {Id = 2};

        public static BotConfiguration Snapshot { get; } = new BotConfiguration
        {
            TelegramAnnouncerBotKey = "cleartext:dummy_botkey",
            TelegramBotId = 741897987,
            TelegramMaxMessagesPerSecond = 15,
            DialogInactivityTimeoutMinutes = 10 * 60,
            MinAllowedVoteCountForWinners = 2,
            MinAllowedContestEntriesToStartVoting = 2,
            MaxTaskSelectionTimeHours = 4,
            MaxAdminVotingTimeHoursSinceFirstVote = 1,
            AnnouncementTimeZone = "Europe/Moscow",
            AnnouncementTimeDescriptor = "МСК",
            AnnouncementDateTimeFormat = "dd.MM HH:mm z",
            VotingChannelInviteLink = "https://t.me/joinchat/test-joinchat-link",
            MinVoteValue = 1,
            MaxVoteValue = 5,
            SubmissionTimeoutMinutes = 20,
            ContestDeadlineEventPreviewTimeHours = 8,
            VotingDeadlineEventPreviewTimeHours = 12,

            Deployments = new[]
            {
                new BotDeployment {Name = "Mockup", MainChatId = MainChat.Id, VotingChatId = VotingChat.Id}
            }
        };

        public static User MockBotUser { get; } = new User
        {
            Id = 0,
            IsBot = true,
            Username = "mockbot",
            FirstName = "mockbot"
        };

        public static int CreateNewMockMessageId()
        {
            return Interlocked.Increment(ref _mockMessageIdAutoincrement);
        }

        public static int GetNewMockUserId()
        {
            return Interlocked.Increment(ref _mockUserIdAutoincrement);
        }

        public static long CreateNewPrivateChatId()
        {
            return Interlocked.Increment(ref _mockUserPrivateChatsId);
        }
    }
}