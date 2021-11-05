using musicallychallenged.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tests.Mockups
{
    class MockBotConfiguration : IBotConfiguration
    {
        //Set cleartext data with cleartext: prefix, they would be replaced with protected text on load
        [ProtectedString]
        public string TelegramAnnouncerBotKey { get; set; } = "cleartext:dummy_botkey";

        public int TelegramBotId { get; set; } = 1234567;
        public bool CreateRepositoryIfNotExists { get; set; } = true;

        public int TelegramMaxMessagesPerSecond { get; set; } = 15;

        public double DialogInactivityTimeoutMinutes { get; set; } = 10 * 60;

        public int MinAllowedVoteCountForWinners { get; set; } = 2;

        public int MinAllowedContestEntriesToStartVoting { get; set; } = 2;

        public double MaxTaskSelectionTimeHours { get; set; } = 4;

        public double MaxAdminVotingTimeHoursSinceFirstVote { get; set; } = 1;

        public string AnnouncementTimeZone { get; set; } = "Europe/Moscow";

        public string AnnouncementTimeDescriptor { get; set; } = "МСК";

        public string AnnouncementDateTimeFormat { get; set; } = "dd.MM HH:mm z";

        public string VotingChannelInviteLink { get; set; } = "https://t.me/joinchat/test-joinchat-link";

        public int MinVoteValue { get; set; } = 1;

        public int MaxVoteValue { get; set; } = 5;

        public int SubmissionTimeoutMinutes { get; set; } = 20;

        public double ContestDeadlineEventPreviewTimeHours { get; set; } = 8;

        public double VotingDeadlineEventPreviewTimeHours { get; set; } = 12;
        public double TaskSuggestionCollectionDeadlineEventPreviewTimeHours { get; set; } = 0.5;

        public double PostponeHoursAllowed { get; set; } = 7 * 2 * 24 + 1; //two weeks plus 1 hour for last-time entries

        public PostponeOption[] PostponeOptions { get; set; } = {
            new PostponeOption(15, PostponeOption.DurationKind.Minutes),
            new PostponeOption(1, PostponeOption.DurationKind.Days),
            new PostponeOption(3, PostponeOption.DurationKind.Days),
            new PostponeOption(7, PostponeOption.DurationKind.Days),
        };

        public BotDeployment[] Deployments { get; set; } = new BotDeployment[]
            {
                new BotDeployment {Name = "Mockup", MainChatId = MockConfiguration.MainChat.Id, VotingChatId = MockConfiguration.VotingChat.Id}
            };

        public int DeadlinePollingPeriodMs { get; set; } = 10;

        //How many users required to trigger postpone
        public int PostponeQuorum { get; set; } = 3;

        public int TaskSuggestionCollectionDeadlineTimeHours { get; set; } = 12;
        public int TaskSuggestionCollectionExtendTimeHours { get; set; } = 24 * 3;
        public int TaskSuggestionCollectionMaxExtendTimeHours { get; set; } = 24 * 7;

        public int TaskSuggestionVotingDeadlineTimeHours { get; set; } = 12;

        public int MinSuggestionVoteValue { get; set; } = -1;

        public int MaxSuggestionVoteValue { get; set; } = 1;
        public string RulesURL { get; set; } = "https://telegra.ph/FAQ-po-PesnoPiscu-02-10";
        public int MinSuggestedTasksBeforeVotingStarts { get; set; } = 2;

        public bool Reload() =>
            true;

        public void Save() { }
    }
}
