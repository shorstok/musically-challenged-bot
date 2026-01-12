using FluentMigrator;

namespace musicallychallenged.Data.Migrations
{
    [Migration(202601120001)]
    public class CreatePesnocoinLedgerMigration : Migration
    {
        public override void Up()
        {
            Create.Table("LedgerAccount")
                .WithColumn("Id").AsInt64().NotNullable().PrimaryKey().Identity()
                .WithColumn("Code").AsString(255).NotNullable()
                .WithColumn("Name").AsString(255).NotNullable()
                .WithColumn("NormalBalance").AsInt32().NotNullable()
                .WithColumn("Kind").AsInt32().NotNullable()
                .WithColumn("UserId").AsInt64().Nullable();

            Create.Index("IX_LedgerAccount_Code")
                .OnTable("LedgerAccount")
                .OnColumn("Code")
                .Unique();

            Create.Index("IX_LedgerAccount_UserId")
                .OnTable("LedgerAccount")
                .OnColumn("UserId");

            Create.Table("LedgerTransaction")
                .WithColumn("Id").AsInt64().NotNullable().PrimaryKey().Identity()
                .WithColumn("CreatedAtUTC").AsString(255).NotNullable()
                .WithColumn("Type").AsInt32().NotNullable()
                .WithColumn("ExternalKey").AsString(255).Nullable()
                .WithColumn("Memo").AsString(1024).Nullable();

            Create.Index("IX_LedgerTransaction_ExternalKey")
                .OnTable("LedgerTransaction")
                .OnColumn("ExternalKey")
                .Unique();

            Create.Table("LedgerEntry")
                .WithColumn("Id").AsInt64().NotNullable().PrimaryKey().Identity()
                .WithColumn("TransactionId").AsInt64().NotNullable()
                .WithColumn("AccountId").AsInt64().NotNullable()
                .WithColumn("Direction").AsInt32().NotNullable()
                .WithColumn("Amount").AsInt64().NotNullable();

            Create.Index("IX_LedgerEntry_AccountId")
                .OnTable("LedgerEntry")
                .OnColumn("AccountId");

            Create.Index("IX_LedgerEntry_TransactionId")
                .OnTable("LedgerEntry")
                .OnColumn("TransactionId");

            SeedSystemAccounts();
            BackfillUserWalletAccounts();
            BackfillOpeningBalances();
        }

        public override void Down()
        {
            Delete.Table("LedgerEntry");
            Delete.Table("LedgerTransaction");
            Delete.Table("LedgerAccount");
        }

        private void SeedSystemAccounts()
        {
            Execute.Sql(@"
INSERT INTO LedgerAccount (Code, Name, NormalBalance, Kind, UserId)
SELECT 'SYS_REWARDS_EXPENSE', 'Rewards Expense', 0, 0, NULL
WHERE NOT EXISTS (SELECT 1 FROM LedgerAccount WHERE Code = 'SYS_REWARDS_EXPENSE');
");

            Execute.Sql(@"
INSERT INTO LedgerAccount (Code, Name, NormalBalance, Kind, UserId)
SELECT 'SYS_POSTPONE_ESCROW', 'Postpone Escrow', 1, 2, NULL
WHERE NOT EXISTS (SELECT 1 FROM LedgerAccount WHERE Code = 'SYS_POSTPONE_ESCROW');
");

            Execute.Sql(@"
INSERT INTO LedgerAccount (Code, Name, NormalBalance, Kind, UserId)
SELECT 'SYS_POSTPONE_REVENUE', 'Postpone Fee Revenue', 1, 3, NULL
WHERE NOT EXISTS (SELECT 1 FROM LedgerAccount WHERE Code = 'SYS_POSTPONE_REVENUE');
");
        }

        private void BackfillUserWalletAccounts()
        {
            Execute.Sql(@"
INSERT INTO LedgerAccount (Code, Name, NormalBalance, Kind, UserId)
SELECT 'USER_WALLET:' || u.Id, 'User Wallet ' || u.Id, 1, 1, u.Id
FROM User u
WHERE NOT EXISTS (
    SELECT 1 FROM LedgerAccount a WHERE a.Code = 'USER_WALLET:' || u.Id
);
");
        }

        private void BackfillOpeningBalances()
        {
            Execute.Sql(@"
INSERT INTO LedgerTransaction (CreatedAtUTC, Type, ExternalKey, Memo)
SELECT strftime('%Y-%m-%dT%H:%M:%SZ', 'now'), 0, 'OPENING_BALANCE:' || u.Id, 'Opening balance from legacy Pesnocent'
FROM User u
WHERE u.Pesnocent > 0
  AND NOT EXISTS (
    SELECT 1 FROM LedgerTransaction t WHERE t.ExternalKey = 'OPENING_BALANCE:' || u.Id
);
");

            Execute.Sql(@"
INSERT INTO LedgerEntry (TransactionId, AccountId, Direction, Amount)
SELECT t.Id, a.Id, 0, u.Pesnocent
FROM User u
JOIN LedgerTransaction t ON t.ExternalKey = 'OPENING_BALANCE:' || u.Id
JOIN LedgerAccount a ON a.Code = 'SYS_REWARDS_EXPENSE'
WHERE u.Pesnocent > 0
  AND NOT EXISTS (
    SELECT 1 FROM LedgerEntry e WHERE e.TransactionId = t.Id AND e.AccountId = a.Id
);
");

            Execute.Sql(@"
INSERT INTO LedgerEntry (TransactionId, AccountId, Direction, Amount)
SELECT t.Id, a.Id, 1, u.Pesnocent
FROM User u
JOIN LedgerTransaction t ON t.ExternalKey = 'OPENING_BALANCE:' || u.Id
JOIN LedgerAccount a ON a.Code = 'USER_WALLET:' || u.Id
WHERE u.Pesnocent > 0
  AND NOT EXISTS (
    SELECT 1 FROM LedgerEntry e WHERE e.TransactionId = t.Id AND e.AccountId = a.Id
);
");
        }
    }
}
