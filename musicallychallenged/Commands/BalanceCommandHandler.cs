using System;
using System.Threading.Tasks;
using musicallychallenged.Config;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Services.Ledger;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Types.Enums;

namespace musicallychallenged.Commands
{
    public class BalanceCommandHandler : ITelegramCommandHandler
    {
        private readonly ILedgerService _ledgerService;
        private readonly LocStrings _loc;
        private readonly IBotConfiguration _configuration;

        public string CommandName { get; } = Schema.BalanceCommandName;
        public string UserFriendlyDescription => _loc.BalanceCommandHandler_Description;

        public BalanceCommandHandler(ILedgerService ledgerService, LocStrings loc, IBotConfiguration configuration)
        {
            _ledgerService = ledgerService;
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

            await dialog.TelegramClient.SendTextMessageAsync(
                dialog.ChatId,
                LocTokens.SubstituteTokens(_loc.BalanceCommandHandler_Message,
                    Tuple.Create(LocTokens.User, user.GetUsernameOrNameWithCircumflex()),
                    Tuple.Create(LocTokens.Balance, $"{pesnocoins:F2}"),
                    Tuple.Create(LocTokens.SubmissionReward, $"{submissionReward:F2}"),
                    Tuple.Create(LocTokens.TaskSuggestionReward, $"{taskSuggestionReward:F2}"),
                    Tuple.Create(LocTokens.VoteReward, $"{voteReward:F2}"),
                    Tuple.Create(LocTokens.PostponeCost, $"{postponeCost:F2}")),
                ParseMode.Html);
        }
    }
}
