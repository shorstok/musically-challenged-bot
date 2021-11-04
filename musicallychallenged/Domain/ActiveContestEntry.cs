using System;
using Dapper.Contrib.Extensions;
using NodaTime;

namespace musicallychallenged.Domain
{
    [Table("ActiveContestEntry")]
    public class ActiveContestEntry : IVotable
    {
        [Key]
        public int Id { get; set; }

        public long AuthorUserId { get; set; }
        public int ChallengeRoundNumber { get; set; }

        public Instant Timestamp { get; set; }

        public int? ConsolidatedVoteCount { get; set; }

        public long ContainerChatId { get; set; }
        public int ContainerMesssageId { get; set; }
        public int ForwardedPayloadMessageId { get; set; }
        public string Description { get; set; }
    }
}