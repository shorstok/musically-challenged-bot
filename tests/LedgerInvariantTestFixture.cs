using System.Linq;
using Dapper;
using musicallychallenged.Domain.Ledger;
using NUnit.Framework;
using tests.DI;

namespace tests
{
    [TestFixture]
    public class LedgerInvariantTestFixture
    {
        [Test]
        public void LedgerTransactionsAreBalanced()
        {
            using var compartment = new TestCompartment(TestContext.CurrentContext);

            var user = compartment.Repository.CreateOrGetUserByTgIdentity(new Telegram.Bot.Types.User
            {
                Id = 9001,
                FirstName = "ledger-test"
            });

            compartment.LedgerService.GetUserWalletBalancePesnocents(user.Id);
            compartment.LedgerService.AwardToUser(
                user.Id,
                100,
                LedgerTransactionType.RewardSubmission,
                "test_reward:9001",
                "Test reward");

            using var connection = TestCompartment.GetRepositoryDbConnection(compartment.Repository);
            var transactions = connection.Query<LedgerTransaction>("SELECT * FROM LedgerTransaction").ToArray();
            var entries = connection.Query<LedgerEntry>("SELECT * FROM LedgerEntry").ToArray();

            Assert.That(transactions.Length, Is.GreaterThan(0), "Expected ledger transactions to exist");

            foreach (var transaction in transactions)
            {
                var txEntries = entries.Where(e => e.TransactionId == transaction.Id).ToArray();

                var debitTotal = txEntries
                    .Where(e => e.Direction == EntryDirection.Debit)
                    .Sum(e => e.Amount);

                var creditTotal = txEntries
                    .Where(e => e.Direction == EntryDirection.Credit)
                    .Sum(e => e.Amount);

                Assert.That(debitTotal, Is.EqualTo(creditTotal),
                    $"Ledger transaction {transaction.Id} is unbalanced");
            }
        }
    }
}
