using System;
using System.Data;
using log4net;
using musicallychallenged.Data;
using musicallychallenged.Domain.Ledger;
using musicallychallenged.Logging;
using NodaTime;

namespace musicallychallenged.Services.Ledger
{
    public class LedgerService : ILedgerService
    {
        private static readonly ILog Logger = Log.Get(typeof(LedgerService));

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IClock _clock;

        public LedgerService(IDbConnectionFactory connectionFactory, IClock clock)
        {
            _connectionFactory = connectionFactory;
            _clock = clock;
        }

        public long GetUserWalletBalancePesnocents(long userId)
        {
            using (var connection = _connectionFactory.CreateOpenConnection())
            using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                var balance = LedgerStore.GetUserWalletBalance(connection, tx, userId, _clock.GetCurrentInstant());
                tx.Commit();
                return balance;
            }
        }

        public void AwardToUser(long userId, long amountPesnocents, LedgerTransactionType type, string externalKey, string memo)
        {
            PostTransaction(
                type,
                externalKey,
                memo,
                (LedgerStore.SysRewardsExpenseCode, EntryDirection.Debit, amountPesnocents),
                (LedgerStore.GetUserWalletCode(userId), EntryDirection.Credit, amountPesnocents));
        }

        public bool TryHoldFromUserToPostponeEscrow(long userId, long amountPesnocents, string externalKey, string memo)
        {
            try
            {
                PostTransactionInternal(
                    LedgerTransactionType.PostponeHold,
                    externalKey,
                    memo,
                    (LedgerStore.GetUserWalletCode(userId), EntryDirection.Debit, amountPesnocents),
                    (LedgerStore.SysPostponeEscrowCode, EntryDirection.Credit, amountPesnocents));

                return true;
            }
            catch (InvalidOperationException)
            {
                Logger.Info($"Insufficient wallet balance for user {userId} when holding {amountPesnocents}");
                return false;
            }
        }

        public void RefundPostponeEscrowToUser(long userId, long amountPesnocents, string externalKey, string memo)
        {
            PostTransaction(
                LedgerTransactionType.PostponeRefund,
                externalKey,
                memo,
                (LedgerStore.SysPostponeEscrowCode, EntryDirection.Debit, amountPesnocents),
                (LedgerStore.GetUserWalletCode(userId), EntryDirection.Credit, amountPesnocents));
        }

        public void CapturePostponeEscrowToRevenue(long amountPesnocents, string externalKey, string memo)
        {
            PostTransaction(
                LedgerTransactionType.PostponeCapture,
                externalKey,
                memo,
                (LedgerStore.SysPostponeEscrowCode, EntryDirection.Debit, amountPesnocents),
                (LedgerStore.SysPostponeRevenueCode, EntryDirection.Credit, amountPesnocents));
        }

        public void PostTransaction(LedgerTransactionType type, string externalKey, string memo,
            params (string accountCode, EntryDirection dir, long amount)[] entries)
        {
            PostTransactionInternal(type, externalKey, memo, entries);
        }

        private bool PostTransactionInternal(LedgerTransactionType type, string externalKey, string memo,
            params (string accountCode, EntryDirection dir, long amount)[] entries)
        {
            using (var connection = _connectionFactory.CreateOpenConnection())
            using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                var applied = LedgerStore.TryPostTransaction(
                    connection,
                    tx,
                    type,
                    externalKey,
                    memo,
                    _clock.GetCurrentInstant(),
                    entries);

                tx.Commit();
                return applied;
            }
        }
    }
}
