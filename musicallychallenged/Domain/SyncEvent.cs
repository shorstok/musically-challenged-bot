using Dapper.Contrib.Extensions;
using musicallychallenged.Services.Sync.DTO;
using NodaTime;

namespace musicallychallenged.Domain
{
    [Table("SyncEvent")]
    public class SyncEvent
    {
        [Key]
        public int Id { get; set; }
        
        public Instant CreatedAt { get; set; }
        public Instant? SyncedAt { get; set; }

        /// <summary>
        ///     Serialized instance of <see cref="SyncEventDto"/>
        /// </summary>
        public string SerializedDto { get; set; }
    }
}