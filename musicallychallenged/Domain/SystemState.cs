using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper.Contrib.Extensions;
using NodaTime;
using Telegram.Bot.Types;

namespace musicallychallenged.Domain
{
    public enum ContestState : int
    {
        Standby = 0,
        Contest = 1,
        Voting = 2,
        FinalizingVotingRound = 3,
        ChoosingNextTask = 4,
        InnerCircleVoting = 5,
        TaskSuggestionCollection = 6,
        TaskSuggestionVoting = 7,
    }

    public enum SelectedTaskKind : int
    {
        Manual = 0,
        Random = 1,
        Poll = 2,
    }

    [Table("SystemState")]
    public class SystemState
    {
        [ExplicitKey]
        public int Id { get; set; }

        public ContestState State { get; set; }
        
        public int CurrentChallengeRoundNumber { get; set; }
        
        public Instant Timestamp { get; set; }

        public long? VotingChannelId { get; set; }
        public long? MainChannelId { get; set; }

        public int? CurrentWinnerId { get; set; }
        public SelectedTaskKind CurrentTaskKind { get; set; }
        public string CurrentTaskTemplate { get; set; }

        public Tuple<SelectedTaskKind, string> CurrentTaskInfo =>
            Tuple.Create(CurrentTaskKind, CurrentTaskTemplate);

        public int? CurrentTaskMessagelId { get; set; }
        public int? CurrentVotingStatsMessageId { get; set; }
        public int? CurrentVotingDeadlineMessageId { get; set; }

        public int? ContestDurationDays { get; set; }
        public int? VotingDurationDays { get; set; }

        public Instant NextDeadlineUTC { get; set; }
        public string PayloadJSON { get; set; }       
    }
}
