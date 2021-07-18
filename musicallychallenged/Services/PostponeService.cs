using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using NodaTime;

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

        private static readonly ILog Logger = Log.Get(typeof(PostponeService));

        private readonly SemaphoreSlim _postponeSemaphore = new SemaphoreSlim(1,1);

        public enum PostponeResult
        {
            Accepted,
            AcceptedAndPostponed,
            DeniedNoQuotaLeft,
            DeniedAlreadyHasOpen,
            GeneralFailure
        }

        public PostponeService(IRepository repository,
            IBotConfiguration configuration,
            IClock clock,
            Lazy<ContestController> contestController,
            Lazy<VotingController> votingController,
            LocStrings loc)
        {
            _repository = repository;
            _configuration = configuration;
            _clock = clock;
            _contestController = contestController;
            _votingController = votingController;
            _loc = loc;
        }

        public async Task CloseAllPostponeRequests(PostponeRequestState finalState)
        {
            if (!await _postponeSemaphore.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                Logger.Error($"Could not acquire semaphore lock");
                return;
            }

            try
            {
                _repository.CloseAllPostponeRequests(finalState);
            }
            finally
            {
                _postponeSemaphore.Release();
            }
        }

        public async Task<PostponeResult> DemandPostponeRequest(User author, Duration postponeDuration)
        {
            if (author == null) throw new ArgumentNullException(nameof(author));

            /*
             * attempt to submit postpone request can result in following:
             *  1. denied: requested duration is exceeds postpone quota left for current round
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

                var usedMinutes = _repository.GetUsedPostponeQuotaForCurrentRoundMinutes();

                if (Duration.FromMinutes(usedMinutes) + postponeDuration >
                    Duration.FromHours(_configuration.PostponeHoursAllowed))
                {
                    Logger.Info(
                        $"User {author.GetUsernameOrNameWithCircumflex()} already requested postpone for {postponeDuration}, but "+
                        $"{usedMinutes} minutes are used already, this plus {postponeDuration} exceeds {_configuration.PostponeHoursAllowed} total hours allowed");

                    return PostponeResult.DeniedNoQuotaLeft;
                }

                var openRequests = _repository.CreatePostponeRequestRetrunOpen(author, postponeDuration);

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
