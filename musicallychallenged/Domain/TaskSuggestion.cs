using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper.Contrib.Extensions;
using NodaTime;

namespace musicallychallenged.Domain
{
    [Table("TaskSuggestion")]
    public class TaskSuggestion : IVotable
    {
        [Key]
        public int Id { get; set; }

        public long AuthorUserId { get; set; }
        public int PollId { get; set; }

        public Instant Timestamp { get; set; }

        /* Task suggestion description (unescaped) */
        public string Description { get; set; }
        public int? ConsolidatedVoteCount { get; set; }

        public long ContainerChatId { get; set; }
        public int ContainerMesssageId { get; set; }
    }
}
