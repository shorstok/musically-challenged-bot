using Dapper.Contrib.Extensions;
using NodaTime;

namespace musicallychallenged.Domain
{
    public enum PostponeRequestState
    {
        Open = 0,
        ClosedSatisfied = 1,
        ClosedDiscarded = 2
    }

    [Table("PostponeRequest")]
    public class PostponeRequest
    {        
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        public int ChallengeRoundNumber { get; set; }

        public long AmountMinutes { get; set; }

        public PostponeRequestState State { get; set; }

        public Instant Timestamp { get; set; }

        public long CostPesnocents { get; set; }
    }
}