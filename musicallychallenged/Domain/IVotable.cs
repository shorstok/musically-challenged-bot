using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace musicallychallenged.Domain
{
    public interface IVotable
    {
        int Id { get; set; }

        int AuthorUserId { get; set; }

        int? ConsolidatedVoteCount { get; set; }

        long ContainerChatId { get; set; }
        int ContainerMesssageId { get; set; }

        string Description { get; set; }
    }
}
