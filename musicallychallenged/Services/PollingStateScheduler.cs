using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Logging;
using musicallychallenged.Services.Events;
using NodaTime;

namespace musicallychallenged.Services
{
    public class PollingStateScheduler : IStateScheduler, IDisposable
    {
        private static readonly ILog logger = Log.Get(typeof(PollingStateScheduler));

        private readonly IRepository _repository;
        private readonly IClock _clock;
        private readonly ContestController _contestController;
        private readonly IEventAggregator _eventAggregator;
        private readonly IBotConfiguration _botConfiguration;
        private volatile bool _stopIssued = false;


        public event Action PreviewDeadlineHit;
        public event Action DeadlineHit;

        private ISubscription[] _subscriptions = new ISubscription[0];

        public PollingStateScheduler(IRepository repository,
            IClock clock,
            ContestController contestController,
            IEventAggregator eventAggregator,
            IBotConfiguration botConfiguration)
        {
            _repository = repository;
            _clock = clock;
            _contestController = contestController;
            _eventAggregator = eventAggregator;
            _botConfiguration = botConfiguration;

            _subscriptions = new ISubscription[]
            {
                _eventAggregator.Subscribe<DemandFastForwardEvent>(OnFastForwardRequested)
            };
        }

        private async void OnFastForwardRequested(DemandFastForwardEvent demandFastForwardEvent)
        {
            var state = _repository.GetOrCreateCurrentState();

            if (demandFastForwardEvent.IsPreDeadline)
            {
                _repository.UpdateState(x => x.NextDeadlineUTC, _clock.GetCurrentInstant().Plus(GetPreDeadlineDuration(state.State)));
            }
            else
            {
                _repository.UpdateState(x => x.NextDeadlineUTC, _clock.GetCurrentInstant());
            }

            await _contestController.UpdateCurrentTaskMessage();
        }

        public async Task Activate()
        {
            bool previewSignaled = false;
            bool deadlineSignaled = false;
            ContestState? lastState = null;

            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_botConfiguration.DeadlinePollingPeriodMs)).ConfigureAwait(false);

                if (_stopIssued)
                    return;

                var state = _repository.GetOrCreateCurrentState();

                if (!IsTimeBoundState(state.State))
                    continue;

                if (lastState != state.State)
                {
                    logger.Info($"Detected state change from {lastState?.ToString() ?? "<null state>"} to {state.State}, resetting signaled status");

                    await Task.Delay(_botConfiguration.DeadlinePollingPeriodMs).ConfigureAwait(false);

                    lastState = state.State;
                    previewSignaled = false;
                    deadlineSignaled = false;
                }

                if (_stopIssued)
                    return;

                state = _repository.GetOrCreateCurrentState();

                var deadline = state.NextDeadlineUTC;

                var now = _clock.GetCurrentInstant();

                if (!previewSignaled && now >= GetPreviewInstantTime(deadline, state.State))
                {
                    OnPreviewDeadlineHit();
                    previewSignaled = true;
                    continue;
                }

                if (!deadlineSignaled && now >= deadline)
                {
                    OnDeadlineHit();
                    deadlineSignaled = true;
                    continue;
                }

            } while (!_stopIssued);
        }

        private static readonly ContestState[] _timeBoundStates = new ContestState[]
        { 
            ContestState.Contest,
            ContestState.Voting,
            ContestState.TaskSuggestionCollection,
            ContestState.TaskSuggestionVoting,
        };

        private bool IsTimeBoundState(ContestState state) =>
            _timeBoundStates.Contains(state);

        private Duration GetPreDeadlineDuration(ContestState state)
        {
            switch (state)
            {

                case ContestState.Contest:

                    return Duration.FromHours(_botConfiguration.ContestDeadlineEventPreviewTimeHours);

                    
                case ContestState.Voting:

                    return Duration.FromHours(_botConfiguration.VotingDeadlineEventPreviewTimeHours);
                    
                default:
                    return Duration.Zero;
                    
            }

        }

        private Instant GetPreviewInstantTime(Instant deadline, ContestState state) => 
            deadline.Minus(GetPreDeadlineDuration(state));

        public void Stop() => _stopIssued = true;
        
        protected virtual void OnPreviewDeadlineHit() => 
            PreviewDeadlineHit?.Invoke();

        protected virtual void OnDeadlineHit() => 
            DeadlineHit?.Invoke();

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }
    }
}