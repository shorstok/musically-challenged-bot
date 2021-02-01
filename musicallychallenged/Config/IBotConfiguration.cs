using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace musicallychallenged.Config
{
    public interface IBotConfiguration
    {
        string TelegramAnnouncerBotKey { get; set; }

        int TelegramBotId { get; set; }

        int TelegramMaxMessagesPerSecond { get; set; }

        double DialogInactivityTimeoutMinutes { get; set; }

        int MinAllowedVoteCountForWinners { get; set; }

        int MinAllowedContestEntriesToStartVoting { get; set; }

        double MaxTaskSelectionTimeHours { get; set; }

        double MaxAdminVotingTimeHoursSinceFirstVote { get; set; }

        string AnnouncementTimeZone { get; set; }

        string AnnouncementTimeDescriptor { get; set; }

        string AnnouncementDateTimeFormat { get; set; }

        string VotingChannelInviteLink { get; set; }

        int MinVoteValue { get; set; }

        int MaxVoteValue { get; set; }

        int SubmissionTimeoutMinutes { get; set; }

        double ContestDeadlineEventPreviewTimeHours { get; set; }

        double VotingDeadlineEventPreviewTimeHours { get; set; }

        double PostponeHoursAllowed { get; set; }

        PostponeOption[] PostponeOptions { get; set; }

        BotDeployment[] Deployments { get; set; }

        int DeadlinePollingPeriodMs { get; set; }

        int PostponeQuorum { get; set; }

        int TaskSuggestionCollectionDeadlineTimeHours { get; set; }

        int TaskSuggestionVotingDeadlineTimeHours { get; set; }

        int MinSuggestionVoteValue { get; set; }

        int MaxSuggestionVoteValue { get; set; }

        bool Reload();

        void Save();
    }
}
