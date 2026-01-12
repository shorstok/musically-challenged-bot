using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Dapper;
using Dapper.Contrib.Extensions;
using musicallychallenged.Domain;
using musicallychallenged.Domain.Ledger;
using NodaTime;

namespace musicallychallenged.Services.Ledger
{
    internal static class LedgerStore
    {
        public const string SysRewardsExpenseCode = "SYS_REWARDS_EXPENSE";
        public const string SysPostponeEscrowCode = "SYS_POSTPONE_ESCROW";
        public const string SysPostponeRevenueCode = "SYS_POSTPONE_REVENUE";
        public const string UserWalletPrefix = "USER_WALLET:";
        private const string OpeningBalancePrefix = "OPENING_BALANCE:";

        public static string GetUserWalletCode(long userId) => $"{UserWalletPrefix}{userId}";

        public static bool TryParseUserWalletCode(string code, out long userId)
        {
            userId = 0;

            if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(UserWalletPrefix, StringComparison.Ordinal))
                return false;

            return long.TryParse(code.Substring(UserWalletPrefix.Length), out userId);
        }

        public static long GetUserWalletBalance(DbConnection connection, DbTransaction tx, long userId, Instant now)
        {
            var account = EnsureUserWalletAccount(connection, tx, userId, now);
            return GetCreditNormalBalance(connection, tx, account.Id);
        }

        public static bool TryPostTransaction(
            DbConnection connection,
            DbTransaction tx,
            LedgerTransactionType type,
            string externalKey,
            string memo,
            Instant now,
            params (string accountCode, EntryDirection dir, long amount)[] entries)
        {
            if (entries == null || entries.Length == 0)
                throw new ArgumentException("Ledger transaction must contain entries.", nameof(entries));

            long debitTotal = 0;
            long creditTotal = 0;

            foreach (var entry in entries)
            {
                if (entry.amount <= 0)
                    throw new ArgumentOutOfRangeException(nameof(entries), "Ledger entry amount must be positive.");

                if (entry.dir == EntryDirection.Debit)
                    debitTotal += entry.amount;
                else
                    creditTotal += entry.amount;
            }

            if (debitTotal != creditTotal)
                throw new InvalidOperationException("Ledger transaction is not balanced.");

            if (!string.IsNullOrWhiteSpace(externalKey))
            {
                var existingId = connection.Query<long?>(
                        @"SELECT Id FROM LedgerTransaction WHERE ExternalKey = @ExternalKey",
                        new { ExternalKey = externalKey },
                        transaction: tx)
                    .FirstOrDefault();

                if (existingId.HasValue)
                    return false;
            }

            var codes = entries.Select(e => e.accountCode).Distinct().ToArray();
            var accounts = connection.Query<LedgerAccount>(
                    @"SELECT * FROM LedgerAccount WHERE Code IN @Codes",
                    new { Codes = codes },
                    transaction: tx)
                .ToList();

            var accountByCode = accounts.ToDictionary(a => a.Code, StringComparer.Ordinal);

            foreach (var code in codes)
            {
                if (accountByCode.ContainsKey(code))
                    continue;

                if (!TryParseUserWalletCode(code, out var userId))
                    throw new InvalidOperationException($"Ledger account {code} does not exist.");

                var walletAccount = EnsureUserWalletAccount(connection, tx, userId, now);
                accountByCode[code] = walletAccount;
            }

            EnsureNoNegativeWallets(connection, tx, entries, accountByCode);

            var transactionId = InsertTransaction(connection, tx, type, externalKey, memo, now);

            foreach (var entry in entries)
            {
                var account = accountByCode[entry.accountCode];

                connection.Insert(new LedgerEntry
                {
                    TransactionId = transactionId,
                    AccountId = account.Id,
                    Direction = entry.dir,
                    Amount = entry.amount
                }, tx);
            }

            return true;
        }

        private static void EnsureNoNegativeWallets(
            DbConnection connection,
            DbTransaction tx,
            IReadOnlyCollection<(string accountCode, EntryDirection dir, long amount)> entries,
            IReadOnlyDictionary<string, LedgerAccount> accountByCode)
        {
            var walletChanges = new Dictionary<long, long>();

            foreach (var entry in entries)
            {
                var account = accountByCode[entry.accountCode];

                if (account.Kind != LedgerAccountKind.UserWallet)
                    continue;

                var delta = entry.dir == EntryDirection.Credit ? entry.amount : -entry.amount;

                if (walletChanges.TryGetValue(account.Id, out var existing))
                    walletChanges[account.Id] = existing + delta;
                else
                    walletChanges[account.Id] = delta;
            }

            foreach (var change in walletChanges)
            {
                if (change.Value >= 0)
                    continue;

                var currentBalance = GetCreditNormalBalance(connection, tx, change.Key);
                var projectedBalance = currentBalance + change.Value;

                if (projectedBalance < 0)
                    throw new InvalidOperationException("Insufficient wallet balance.");
            }
        }

        private static LedgerAccount EnsureUserWalletAccount(DbConnection connection, DbTransaction tx, long userId, Instant now)
        {
            var code = GetUserWalletCode(userId);
            var existing = GetLedgerAccountByCode(connection, tx, code);

            if (existing != null)
                return existing;

            var account = new LedgerAccount
            {
                Code = code,
                Name = $"User Wallet {userId}",
                NormalBalance = NormalBalance.Credit,
                Kind = LedgerAccountKind.UserWallet,
                UserId = userId
            };

            account.Id = connection.Insert(account, tx);

            EnsureOpeningBalance(connection, tx, userId, account.Id, now);

            return account;
        }

        private static void EnsureOpeningBalance(DbConnection connection, DbTransaction tx, long userId, long walletAccountId, Instant now)
        {
            var externalKey = $"{OpeningBalancePrefix}{userId}";

            var existingId = connection.Query<long?>(
                    @"SELECT Id FROM LedgerTransaction WHERE ExternalKey = @ExternalKey",
                    new { ExternalKey = externalKey },
                    transaction: tx)
                .FirstOrDefault();

            if (existingId.HasValue)
                return;

            var user = connection.Get<User>(userId, tx);

            if (user == null || user.Pesnocent <= 0)
                return;

            var rewardsAccount = GetLedgerAccountByCode(connection, tx, SysRewardsExpenseCode);

            if (rewardsAccount == null)
                throw new InvalidOperationException("SYS_REWARDS_EXPENSE account missing.");

            var transactionId = InsertTransaction(
                connection,
                tx,
                LedgerTransactionType.OpeningBalance,
                externalKey,
                "Opening balance from legacy Pesnocent",
                now);

            connection.Insert(new LedgerEntry
            {
                TransactionId = transactionId,
                AccountId = rewardsAccount.Id,
                Direction = EntryDirection.Debit,
                Amount = user.Pesnocent
            }, tx);

            connection.Insert(new LedgerEntry
            {
                TransactionId = transactionId,
                AccountId = walletAccountId,
                Direction = EntryDirection.Credit,
                Amount = user.Pesnocent
            }, tx);
        }

        private static long GetCreditNormalBalance(DbConnection connection, DbTransaction tx, long accountId)
        {
            var totals = connection.QuerySingleOrDefault<BalanceTotals>(
                @"SELECT
                    COALESCE(SUM(CASE WHEN Direction = @Credit THEN Amount ELSE 0 END), 0) AS Credits,
                    COALESCE(SUM(CASE WHEN Direction = @Debit THEN Amount ELSE 0 END), 0) AS Debits
                FROM LedgerEntry
                WHERE AccountId = @AccountId",
                new
                {
                    AccountId = accountId,
                    Credit = EntryDirection.Credit,
                    Debit = EntryDirection.Debit
                },
                transaction: tx);

            return (totals?.Credits ?? 0) - (totals?.Debits ?? 0);
        }

        private static LedgerAccount GetLedgerAccountByCode(DbConnection connection, DbTransaction tx, string code)
        {
            return connection.Query<LedgerAccount>(
                    @"SELECT * FROM LedgerAccount WHERE Code = @Code",
                    new { Code = code },
                    transaction: tx)
                .FirstOrDefault();
        }

        private static long InsertTransaction(
            DbConnection connection,
            DbTransaction tx,
            LedgerTransactionType type,
            string externalKey,
            string memo,
            Instant now)
        {
            var transaction = new LedgerTransaction
            {
                CreatedAtUTC = now,
                Type = type,
                ExternalKey = externalKey,
                Memo = memo
            };

            return connection.Insert(transaction, tx);
        }

        private class BalanceTotals
        {
            public long Credits { get; set; }
            public long Debits { get; set; }
        }
    }
}
