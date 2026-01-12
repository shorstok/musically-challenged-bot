using musicallychallenged.Domain.Ledger;

namespace musicallychallenged.Services.Ledger
{
    public interface ILedgerService
    {
        long GetUserWalletBalancePesnocents(long userId);
        void AwardToUser(long userId, long amountPesnocents, LedgerTransactionType type, string externalKey, string memo);
        bool TryHoldFromUserToPostponeEscrow(long userId, long amountPesnocents, string externalKey, string memo);
        void RefundPostponeEscrowToUser(long userId, long amountPesnocents, string externalKey, string memo);
        void CapturePostponeEscrowToRevenue(long amountPesnocents, string externalKey, string memo);
        void PostTransaction(LedgerTransactionType type, string externalKey, string memo,
            params (string accountCode, EntryDirection dir, long amount)[] entries);
    }
}
