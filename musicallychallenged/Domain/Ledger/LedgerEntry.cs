using Dapper.Contrib.Extensions;

namespace musicallychallenged.Domain.Ledger
{
    [Table("LedgerEntry")]
    public class LedgerEntry
    {
        [Key]
        public long Id { get; set; }

        public long TransactionId { get; set; }
        public long AccountId { get; set; }

        public EntryDirection Direction { get; set; }
        public long Amount { get; set; }
    }
}
