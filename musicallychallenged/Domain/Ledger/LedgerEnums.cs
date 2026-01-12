namespace musicallychallenged.Domain.Ledger
{
    public enum NormalBalance
    {
        Debit = 0,
        Credit = 1
    }

    public enum EntryDirection
    {
        Debit = 0,
        Credit = 1
    }

    public enum LedgerAccountKind
    {
        SysRewardsExpense = 0,
        UserWallet = 1,
        SysPostponeEscrow = 2,
        SysPostponeRevenue = 3,
        SysTestSink = 4
    }

    public enum LedgerTransactionType
    {
        OpeningBalance = 0,
        RewardSubmission = 1,
        RewardMidvoteSubmission = 2,
        RewardVote = 3,
        RewardTaskSuggestion = 4,
        PostponeHold = 5,
        PostponeRefund = 6,
        PostponeCapture = 7,
        LegacyAdjustment = 8
    }
}
