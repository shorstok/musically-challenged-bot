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

        private readonly ISubscription[] _subscriptions;

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

        private record SignaledDescriptor(Instant Deadline, bool Signaled);

        public async Task Activate()
        {
            ContestState? lastState = null;

            var preview = new SignaledDescriptor(Instant.MinValue, Signaled: false);
            var final = new SignaledDescriptor(Instant.MinValue, Signaled: false);

            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_botConfiguration.DeadlinePollingPeriodMs)).ConfigureAwait(false);

                if (_stopIssued)
                    return;

                var state = _repository.GetOrCreateCurrentState();

                if (!state.State.IsTimeBound())
                    continue;

                if (lastState != state.State)
                {
                    logger.Info($"Detected state change from {lastState?.ToString() ?? "<null state>"} to {state.State}, resetting signaled status");

                    await Task.Delay(_botConfiguration.DeadlinePollingPeriodMs).ConfigureAwait(false);

                    lastState = state.State;
                    preview = new SignaledDescriptor(Instant.MinValue, Signaled: false);
                    final = new SignaledDescriptor(Instant.MinValue, Signaled: false);
                }

                if (_stopIssued)
                    return;

                state = _repository.GetOrCreateCurrentState();

                var deadline = state.NextDeadlineUTC;

                var now = _clock.GetCurrentInstant();

                //If deadline is shifted, reset 'preview' signaled status to re-issue a preview message
                //(or to give chance for an extra postpone)

                if (preview.Signaled && preview.Deadline != deadline)
                {
                    logger.Info($"Deadline moved - resetting preview signaled status");
                    preview = preview with { Signaled = false };
                }

                if (!preview.Signaled && now >= GetPreviewInstantTime(deadline, state.State))
                {
                    preview = preview with { Deadline = deadline, Signaled = true};
                    OnPreviewDeadlineHit();
                    continue;
                }

                if (!final.Signaled && now >= deadline)
                {
                    OnDeadlineHit();
                    final = final with { Deadline = deadline, Signaled = true};
                    continue;
                }

            } while (!_stopIssued);
        }

        private Duration GetPreDeadlineDuration(ContestState state) =>
            state switch
            {
                ContestState.Contest => Duration.FromHours(_botConfiguration.ContestDeadlineEventPreviewTimeHours),
                ContestState.Voting => Duration.FromHours(_botConfiguration.VotingDeadlineEventPreviewTimeHours),
                ContestState.TaskSuggestionCollection => Duration.FromHours(_botConfiguration
                    .TaskSuggestionCollectionDeadlineEventPreviewTimeHours),
                _ => Duration.Zero
            };

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