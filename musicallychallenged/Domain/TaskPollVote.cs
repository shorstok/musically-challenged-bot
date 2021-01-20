using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper.Contrib.Extensions;
using NodaTime;

namespace musicallychallenged.Domain
{
    [Table("TaskPollVote")]
    public class TaskPollVote
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public int TaskSuggestionId { get; set; }

        public Instant Timestamp { get; set; }

        public int Value { get; set; }
    }
}
