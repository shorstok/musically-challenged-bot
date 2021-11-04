using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace musicallychallenged.Domain
{
    public interface IVote
    {
        int Id { get; set; }

        long UserId { get; set; }

        int Value { get; set; }
    }
}
