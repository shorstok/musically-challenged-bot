using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Domain.Ledger;
using musicallychallenged.Localization;
using musicallychallenged.Services.Ledger;
using musicallychallenged.Services.Telegram;
using Dapper;
using NodaTime.Text;
using Telegram.Bot.Types.Enums;

namespace musicallychallenged.Commands
{
    public class BalanceCommandHandler : ITelegramCommandHandler
    {
        private readonly ILedgerService _ledgerService;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly LocStrings _loc;
        private readonly IBotConfiguration _configuration;

        public string CommandName { get; } = Schema.BalanceCommandName;
        public string UserFriendlyDescription => _loc.BalanceCommandHandler_Description;

        public BalanceCommandHandler(ILedgerService ledgerService, IDbConnectionFactory connectionFactory, LocStrings loc, IBotConfiguration configuration)
        {
            _ledgerService = ledgerService;
            _connectionFactory = connectionFactory;
            _loc = loc;
            _configuration = configuration;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var pesnocoins = _ledgerService.GetUserWalletBalancePesnocents(user.Id) / 100.0;

            var submissionReward = _configuration.PesnocentsAwardedForTrackSubmission / 100.0;
            var taskSuggestionReward = _configuration.PesnocentsAwardedForTaskSuggestion / 100.0;
            var voteReward = _configuration.PesnocentsAwardedForVote / 100.0;
            var postponeCost = _configuration.PesnocentsRequiredPerPostponeRequest / 100.0;

            var historyText = BuildHistoryText(user.Id);
            var messageText = LocTokens.SubstituteTokens(_loc.BalanceCommandHandler_Message,
                Tuple.Create(LocTokens.User, user.GetUsernameOrNameWithCircumflex()),
                Tuple.Create(LocTokens.Balance, $"{pesnocoins:F2}"),
                Tuple.Create(LocTokens.SubmissionReward, $"{submissionReward:F2}"),
                Tuple.Create(LocTokens.TaskSuggestionReward, $"{taskSuggestionReward:F2}"),
                Tuple.Create(LocTokens.VoteReward, $"{voteReward:F2}"),
                Tuple.Create(LocTokens.PostponeCost, $"{postponeCost:F2}"));

            if (!string.IsNullOrWhiteSpace(historyText))
                messageText = $"{messageText}\n\n{historyText}";

            await dialog.TelegramClient.SendTextMessageAsync(
                dialog.ChatId,
                messageText,
                ParseMode.Html);
        }

        private string BuildHistoryText(long userId)
        {
            const int historyLimit = 10;

            using var connection = _connectionFactory.CreateOpenConnection();

            var rows = connection.Query<LedgerHistoryRow>(
                @"SELECT t.CreatedAtUTC, t.Type,
                         SUM(CASE WHEN e.Direction = @Credit THEN e.Amount ELSE 0 END) -
                         SUM(CASE WHEN e.Direction = @Debit THEN e.Amount ELSE 0 END) AS NetAmount
                  FROM LedgerTransaction t
                  JOIN LedgerEntry e ON e.TransactionId = t.Id
                  JOIN LedgerAccount a ON a.Id = e.AccountId
                  WHERE a.Code = @WalletCode
                  GROUP BY t.Id, t.CreatedAtUTC, t.Type
                  ORDER BY t.CreatedAtUTC DESC
                  LIMIT @Limit",
                new
                {
                    WalletCode = LedgerStore.GetUserWalletCode(userId),
                    Credit = (int)EntryDirection.Credit,
                    Debit = (int)EntryDirection.Debit,
                    Limit = historyLimit
                }).ToArray();

            if (rows.Length == 0)
                return _loc.BalanceCommandHandler_HistoryEmpty;

            var lines = rows
                .Select(FormatHistoryRow)
                .ToArray();

            var header = string.Format(_loc.BalanceCommandHandler_HistoryHeader, rows.Length);
            return $"{header}\n" + string.Join("\n", lines);
        }

        private string FormatHistoryRow(LedgerHistoryRow row)
        {
            var timestamp = FormatTimestamp(row.CreatedAtUTC);
            var amount = row.NetAmount / 100.0;
            var sign = amount >= 0 ? "+" : "-";
            var absAmount = Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture);
            var description = DescribeTransaction((LedgerTransactionType)row.Type);

            return $"• {timestamp} — {sign}{absAmount} ({description})";
        }

        private static string FormatTimestamp(string createdAtUtc)
        {
            var general = InstantPattern.General.Parse(createdAtUtc);
            if (general.Success)
                return general.Value.ToDateTimeUtc().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

            var extended = InstantPattern.ExtendedIso.Parse(createdAtUtc);
            if (extended.Success)
                return extended.Value.ToDateTimeUtc().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

            return createdAtUtc;
        }

        private string DescribeTransaction(LedgerTransactionType type)
        {
            return type switch
            {
                LedgerTransactionType.OpeningBalance => _loc.BalanceCommandHandler_TxOpeningBalance,
                LedgerTransactionType.RewardSubmission => _loc.BalanceCommandHandler_TxRewardSubmission,
                LedgerTransactionType.RewardMidvoteSubmission => _loc.BalanceCommandHandler_TxRewardMidvoteSubmission,
                LedgerTransactionType.RewardVote => _loc.BalanceCommandHandler_TxRewardVote,
                LedgerTransactionType.RewardTaskSuggestion => _loc.BalanceCommandHandler_TxRewardTaskSuggestion,
                LedgerTransactionType.PostponeHold => _loc.BalanceCommandHandler_TxPostponeHold,
                LedgerTransactionType.PostponeRefund => _loc.BalanceCommandHandler_TxPostponeRefund,
                LedgerTransactionType.PostponeCapture => _loc.BalanceCommandHandler_TxPostponeCapture,
                LedgerTransactionType.LegacyAdjustment => _loc.BalanceCommandHandler_TxLegacyAdjustment,
                _ => _loc.BalanceCommandHandler_TxGeneric
            };
        }

        private class LedgerHistoryRow
        {
            public string CreatedAtUTC { get; set; }
            public int Type { get; set; }
            public long NetAmount { get; set; }
        }
    }
}
