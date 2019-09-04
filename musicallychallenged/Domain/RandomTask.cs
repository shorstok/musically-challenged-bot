using Dapper.Contrib.Extensions;
using NodaTime;

namespace musicallychallenged.Domain
{
    [Table("RandomTask")]
    public class RandomTask
    {        
        [Key]
        public long Id { get; set; }

        public string Description{ get; set; }
        public Instant LastUsed { get; set; }      
        public int UsedCount { get; set; }      
        public int Priority { get; set; }      
        public string OriginalAuthorName { get; set; }      
    }
}