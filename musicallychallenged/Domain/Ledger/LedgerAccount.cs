using Dapper.Contrib.Extensions;

namespace musicallychallenged.Domain.Ledger
{
    [Table("LedgerAccount")]
    public class LedgerAccount
    {
        [Key]
        public long Id { get; set; }

        public string Code { get; set; }
        public string Name { get; set; }

        public NormalBalance NormalBalance { get; set; }
        public LedgerAccountKind Kind { get; set; }

        public long? UserId { get; set; }
    }
}
