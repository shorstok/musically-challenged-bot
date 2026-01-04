using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using NodaTime;
using Telegram.Bot.Types.Enums;

namespace musicallychallenged.Services
{
    public class PostponeService
    {
        private readonly IRepository _repository;
        private readonly IBotConfiguration _configuration;
        private readonly IClock _clock;
        private readonly Lazy<ContestController> _contestController;
        private readonly Lazy<VotingController> _votingController;
        private readonly LocStrings _loc;
        private readonly ITelegramClient _client;

        private static readonly ILog Logger = Log.Get(typeof(PostponeService));

        private readonly SemaphoreSlim _postponeSemaphore = new SemaphoreSlim(1,1);

        public enum PostponeResult
        {
            Accepted,
            AcceptedAndPostponed,
            DeniedNoQuotaLeft,
            DeniedAlreadyHasOpen,
            DeniedInsufficientBalance,
            GeneralFailure
        }

        public PostponeService(IRepository repository,
            IBotConfiguration configuration,
            IClock clock,
            Lazy<ContestController> contestController,
            Lazy<VotingController> votingController,
            LocStrings loc,
            ITelegramClient client)
        {
            _repository = repository;
            _configuration = configuration;
            _clock = clock;
            _contestController = contestController;
            _votingController = votingController;
            _loc = loc;
            _client = client;
        }

        public async Task CloseRefundAllPostponeRequests(PostponeRequestState finalState)
        {
            if (!await _postponeSemaphore.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                Logger.Error($"Could not acquire semaphore lock");
                return;
            }

            try
            {
                PostponeRequest[] closedRequests = _repository.CloseRefundAllPostponeRequests(finalState,
                    refund: finalState == PostponeRequestState.ClosedDiscarded);

                if (finalState == PostponeRequestState.ClosedDiscarded)
                {
                    await NotifyEachUserAboutRefund(closedRequests);
                }
            }
            finally
            {
                _postponeSemaphore.Release();
            }
        }

        private async Task NotifyEachUserAboutRefund(PostponeRequest[] requests)
        {
            if (requests == null || requests.Length == 0)
            {
                Logger.Info("No requests to notify about refunds");
                return;
            }

            Logger.Info($"Notifying {requests.Length} users about refunds");

            foreach (var request in requests)
            {
                // Only notify for requests that had a cost
                if (request.CostPesnocents <= 0)
                    continue;

                try
                {
                    // Get user with ChatId
                    var user = _repository.GetExistingUserWithTgId(request.UserId);

                    if (user == null)
                    {
                        Logger.Info($"Cannot notify user {request.UserId} about refund: user not found");
                        continue;
                    }

                    if (user.ChatId == null)
                    {
                        Logger.Warn($"Cannot notify user {user.GetUsernameOrNameWithCircumflex()} about refund: ChatId is null (user may have blocked bot)");
                        continue;
                    }

                    // Format refund amount as pesnocoins (divide by 100)
                    string refundAmount = (request.CostPesnocents / 100.0m).ToString("0.00");

                    var message = LocTokens.SubstituteTokens(
                        _loc.PostponeService_RefundNotification,
                        Tuple.Create(LocTokens.Balance, refundAmount));

                    await _client.SendTextMessageAsync(user.ChatId.Value, message, ParseMode.Html);

                    Logger.Info($"Notified user {user.GetUsernameOrNameWithCircumflex()} about refund of {refundAmount} pesnocoins");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to notify user {request.UserId} about refund for request {request.Id}", ex);
                    // Continue to next user - don't let one failure stop all notifications
                }
            }
        }

        public async Task<PostponeResult> DemandPostponeRequest(User author, Duration postponeDuration)
        {
            if (author == null) throw new ArgumentNullException(nameof(author));

            /*
             * attempt to submit postpone request can result in following:
             *  (Was - disabled now:) 1. denied: requested duration is exceeds postpone quota left for current round
             *  2. denied: user has unclosed postpone request
             *  3. accepted / accepted & postponed: author has no open requests & duration meets quota.
             *      3.1 accepted & postponed if there is already postpone quorum
             */

            if (!await _postponeSemaphore.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                Logger.Error($"Could not acquire semaphore lock");
                return PostponeResult.GeneralFailure;
            }

            try
            {
                var requests = _repository.GetOpenPostponeRequestsForUser(author.Id);

                if (requests.Any())
                {
                    Logger.Info(
                        $"User {author.GetUsernameOrNameWithCircumflex()} already has open request(s): " +
                        $"{string.Join("; ", requests.Select(r => $"{r.AmountMinutes}min. round {r.ChallengeRoundNumber} issued {r.Timestamp}"))}");

                    return PostponeResult.DeniedAlreadyHasOpen;
                }

                // This branch is disabled - we do not limit total postpone quota per user per round
                // var usedMinutes = _repository.GetUsedPostponeQuotaForCurrentRoundMinutes();
                //
                // if (Duration.FromMinutes(usedMinutes) + postponeDuration >
                //     Duration.FromHours(_configuration.PostponeHoursAllowed))
                // {
                //     Logger.Info(
                //         $"User {author.GetUsernameOrNameWithCircumflex()} already requested postpone for {postponeDuration}, but "+
                //         $"{usedMinutes} minutes are used already, this plus {postponeDuration} exceeds {_configuration.PostponeHoursAllowed} total hours allowed");
                //
                //     return PostponeResult.DeniedNoQuotaLeft;
                // }

                // Deduct coins and create postpone request atomically
                var openRequests = _repository.CreatePostponeRequestWithCoinDeduction(author, postponeDuration,
                    _configuration.PesnocentsRequiredPerPostponeRequest,
                    out bool insufficientBalance);

                if (insufficientBalance)
                {
                    Logger.Info($"User {author.GetUsernameOrNameWithCircumflex()} has insufficient balance for postpone vote");
                    return PostponeResult.DeniedInsufficientBalance;
                }

                if (openRequests.GroupBy(r => r.UserId).Count() < _configuration.PostponeQuorum)
                {
                    Logger.Info($"Postpone request from {author.GetUsernameOrNameWithCircumflex()} accepted, total {openRequests.Length} pending " +
                                $"({_configuration.PostponeQuorum} requests constitute quorum)");
                    return PostponeResult.Accepted;
                }

                //Postpone quorum reached - need to close all open requests and pick one to move deadline

                if (!await FinalizePostponeInternal(openRequests))
                {
                    Logger.Error($"FinalizePostponeInternal failed");
                    return PostponeResult.GeneralFailure;
                }

                return PostponeResult.AcceptedAndPostponed;
            }
            finally
            {
                _postponeSemaphore.Release();
            }
        }

        private async Task<bool> FinalizePostponeInternal(PostponeRequest[] openRequests)
        {
            if (openRequests == null) throw new ArgumentNullException(nameof(openRequests));

            if (openRequests.Length < 1)
            { 
                Logger.Error("FinalizePostponeInternal with 0 openRequests");
                return false;
            }

            var keyRequest = openRequests.OrderByDescending(r => r.AmountMinutes).First();

            Logger.Info($"Closing postpone - {openRequests.Length} " +
                        $"({string.Join(", ",openRequests.Select(s=>s.AmountMinutes+" min."))}) requests total, " +
                        $"picked request with {keyRequest.AmountMinutes}min. from userid {keyRequest.UserId}");

            var overriddenDeadline = _repository.GetOrCreateCurrentState().NextDeadlineUTC + 
                                     Duration.FromMinutes(keyRequest.AmountMinutes);

            _repository.UpdateState(x => x.NextDeadlineUTC, overriddenDeadline);

            Logger.Info($"Changed deadline to {overriddenDeadline}");

            _repository.FinalizePostponeRequests(keyRequest);

            await _contestController.Value.UpdateCurrentTaskMessage();
            await _votingController.Value.UpdateCurrentTaskMessage();

            await _contestController.Value.AnnounceNewDeadline(_loc.PostponeService_DeadlinePostponedQuorumFulfilled);

            return true;
        }
    }
}
