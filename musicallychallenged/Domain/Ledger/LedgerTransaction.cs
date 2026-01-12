using Dapper.Contrib.Extensions;
using NodaTime;

namespace musicallychallenged.Domain.Ledger
{
    [Table("LedgerTransaction")]
    public class LedgerTransaction
    {
        [Key]
        public long Id { get; set; }

        public Instant CreatedAtUTC { get; set; }
        public LedgerTransactionType Type { get; set; }

        public string ExternalKey { get; set; }
        public string Memo { get; set; }
    }
}
