using System;
using System.Linq;
using Dapper;
using musicallychallenged.Domain.Ledger;
using musicallychallenged.Services.Ledger;
using tests.DI;

namespace tests
{
    public static class LedgerTestHelper
    {
        private const string TestSinkCode = "SYS_TEST_SINK";

        public static long GetBalance(ILedgerService ledgerService, long userId) =>
            ledgerService.GetUserWalletBalancePesnocents(userId);

        public static void CreditUserToExactBalance(TestCompartment compartment, long userId, long targetBalance)
        {
            var ledgerService = compartment.LedgerService;
            var current = ledgerService.GetUserWalletBalancePesnocents(userId);

            if (current == targetBalance)
                return;

            if (current < targetBalance)
            {
                var delta = targetBalance - current;
                ledgerService.AwardToUser(
                    userId,
                    delta,
                    LedgerTransactionType.LegacyAdjustment,
                    $"test_award:{userId}:{Guid.NewGuid()}",
                    "Test balance top-up");
                return;
            }

            var reduction = current - targetBalance;
            EnsureTestSinkAccount(compartment);

            ledgerService.PostTransaction(
                LedgerTransactionType.LegacyAdjustment,
                $"test_debit:{userId}:{Guid.NewGuid()}",
                "Test balance reduction",
                ($"USER_WALLET:{userId}", EntryDirection.Debit, reduction),
                (TestSinkCode, EntryDirection.Credit, reduction));
        }

        private static void EnsureTestSinkAccount(TestCompartment compartment)
        {
            using var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository);

            var existing = connection.Query<long?>(
                    @"SELECT Id FROM LedgerAccount WHERE Code = @Code",
                    new { Code = TestSinkCode })
                .FirstOrDefault();

            if (existing.HasValue)
                return;

            connection.Execute(
                @"INSERT INTO LedgerAccount (Code, Name, NormalBalance, Kind, UserId)
                  VALUES (@Code, @Name, @NormalBalance, @Kind, NULL)",
                new
                {
                    Code = TestSinkCode,
                    Name = "Test Sink",
                    NormalBalance = (int)NormalBalance.Credit,
                    Kind = (int)LedgerAccountKind.SysTestSink
                });
        }
    }
}
