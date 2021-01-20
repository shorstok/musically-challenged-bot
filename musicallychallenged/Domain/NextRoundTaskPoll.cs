using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper.Contrib.Extensions;
using NodaTime;

namespace musicallychallenged.Domain
{
    public enum NextRoundTaskPollState : int
    {
        Open = 0,
        Closed = 1,
    }

    [Table("NextRoundTaskPoll")]
    public class NextRoundTaskPoll
    {
        [Key]
        public int Id { get; set; }
        public Instant Timestamp { get; set; }
        public NextRoundTaskPollState State { get; set; }
    }
}
