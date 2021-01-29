using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentMigrator.Builders.Alter.Table;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Events;
using musicallychallenged.Services.Telegram;
using Stateless;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public sealed class StateController : IStartable, IDisposable
    {
        private static readonly ILog logger = Log.Get(typeof(StateController));

        private readonly IRepository _repository;
        private readonly IEventAggregator _eventAggregator;
        private readonly BroadcastController _broadcastController;
        private readonly IBotConfiguration _configuration;
        private readonly LocStrings _loc;
        private readonly IStateScheduler _scheduler;
        private readonly ContestController _contestController;
        private readonly Func<NewTaskSelectorController> _taskSelectorGenerator;
        private readonly Func<InnerCircleVotingController> _innerCircleVoteGenerator;
        private readonly DialogManager _dialogManager;
        private readonly ITelegramClient _client;
        private readonly VotingController _votingController;

        private readonly Random _random = new Random();

        private enum Trigger
        {
            PreviewDeadlineHit,
            DeadlineHit,
            Explicit,
            NotEnoughContesters,
            NotEnoughVotes,
            WinnerChosen,
            TaskSelectedByWinner,
            TaskDeclined,
            TaskApproved,
            InitiatedNextRoundTaskPoll,
            TaskSelectedByPoll,
        }

        private SemaphoreSlim _transitionSemaphoreSlim = new SemaphoreSlim(1, 1);
        private const int transitionMaxWaitMs = 60*60*1000;

        private readonly StateMachine<ContestState, Trigger> _stateMachine;

        private ISubscription[] _subscriptions;

        private readonly StateMachine<ContestState, Trigger>.TriggerWithParameters<ContestState>
            _explicitStateSwitchTrigger;

        public StateController(IRepository repository,
            IEventAggregator eventAggregator,
            BroadcastController broadcastController,
            IBotConfiguration configuration,
            LocStrings loc,
            IStateScheduler scheduler,
            ContestController contestController,
            Func<NewTaskSelectorController> taskSelectorGenerator,
            Func<InnerCircleVotingController> innerCircleVoteGenerator,
            DialogManager dialogManager,
            ITelegramClient client,
            VotingController votingController)
        {
            _repository = repository;
            _eventAggregator = eventAggregator;
            _broadcastController = broadcastController;
            _configuration = configuration;
            _loc = loc;
            _scheduler = scheduler;
            _contestController = contestController;
            _taskSelectorGenerator = taskSelectorGenerator;
            _innerCircleVoteGenerator = innerCircleVoteGenerator;
            _dialogManager = dialogManager;
            _client = client;
            _votingController = votingController;

            _scheduler.DeadlineHit += _scheduler_DeadlineHit;
            _scheduler.PreviewDeadlineHit += _scheduler_PreviewDeadlineHit;

            _subscriptions = new ISubscription[]
            {
                _eventAggregator.Subscribe<KickstartContestEvent>(OnKickstartContest),
                _eventAggregator.Subscribe<ChatMigrationFailedEvent>(OnChatMigrationFailed),
                _eventAggregator.Subscribe<DemandStandbyEvent>((s) =>
                    _stateMachine.Fire(_explicitStateSwitchTrigger, ContestState.Standby))
            };


            /*
             * State transition logic : finite state automaton definintion
             */

            _stateMachine =
                new StateMachine<ContestState, Trigger>(GetCurrentState, SetCurrentState, FiringMode.Queued);

            _explicitStateSwitchTrigger = _stateMachine.SetTriggerParameters<ContestState>(Trigger.Explicit);

            //just switch to whatever state was requested
            _stateMachine.Configure(ContestState.Standby).PermitDynamic(_explicitStateSwitchTrigger, state => state);
            _stateMachine.Configure(ContestState.Standby).Permit(Trigger.TaskApproved, ContestState.Contest);

            _stateMachine.Configure(ContestState.Voting).PermitDynamic(_explicitStateSwitchTrigger, state => state);
            _stateMachine.Configure(ContestState.Contest).PermitDynamic(_explicitStateSwitchTrigger, state => state);
            _stateMachine.Configure(ContestState.ChoosingNextTask)
                .PermitDynamic(_explicitStateSwitchTrigger, state => state);
            _stateMachine.Configure(ContestState.InnerCircleVoting)
                .PermitDynamic(_explicitStateSwitchTrigger, state => state);
            _stateMachine.Configure(ContestState.FinalizingVotingRound)
                .PermitDynamic(_explicitStateSwitchTrigger, state => state);

            _stateMachine.Configure(ContestState.TaskSuggestionCollection)
                .PermitDynamic(_explicitStateSwitchTrigger, state => state);
            _stateMachine.Configure(ContestState.TaskSuggestionVoting)
                .PermitDynamic(_explicitStateSwitchTrigger, state => state);


            //State: Voting
            //Voting in progress
            _stateMachine.Configure(ContestState.Voting).
                OnEntry(OnVotingStartedOrResumed).
                //On just before deadline, warn probable winners(s) to be ready for next round task responsibility
                PermitReentry(Trigger.PreviewDeadlineHit).
                //On deadline hit, switch to winner selection
                Permit(Trigger.DeadlineHit, ContestState.FinalizingVotingRound)
                .Permit(Trigger.NotEnoughContesters, ContestState.Standby)
                .Permit(Trigger.NotEnoughVotes, ContestState.Standby);

            //State: FinalizingVotingRound
            //System choses winner
            _stateMachine.Configure(ContestState.FinalizingVotingRound)
                .OnActivate(OnFinalizingActivate,"d")
                .OnEntry(EnteredFinalizingVoting)
                .Permit(Trigger.WinnerChosen, ContestState.ChoosingNextTask)
                .Permit(Trigger.NotEnoughVotes, ContestState.Standby)
                .Permit(Trigger.NotEnoughContesters, ContestState.Standby);

            //State: ChoosingNextTask
            //Winner desribes next task
            _stateMachine.Configure(ContestState.ChoosingNextTask).
                OnActivate(ActivatedChoosingNextTask).
                OnEntry(EnteredChoosingNextTask)
                .Permit(Trigger.TaskSelectedByWinner, ContestState.InnerCircleVoting)
                .Permit(Trigger.InitiatedNextRoundTaskPoll, ContestState.TaskSuggestionCollection);

            //State: InnerCircleVoting
            //Administrators premoderating next task
            _stateMachine.Configure(ContestState.InnerCircleVoting).
                OnActivate(ActivatedInnerCircleVoting).
                OnEntry(EnteredInnerCircleVoting).
                //on approve switch to main contest mode
                Permit(Trigger.TaskApproved, ContestState.Contest).
                //on decline, restart task selection
                Permit(Trigger.TaskDeclined, ContestState.ChoosingNextTask);

            //State: Contest
            //Administrators premoderating next task
            _stateMachine.Configure(ContestState.Contest).
                OnActivate(OnContestActivated).
                OnEntry(OnContestStartedOrResumed).
                //on approve switch to main contest mode
                PermitReentry(Trigger.PreviewDeadlineHit).
                //on decline, restart task selection
                Permit(Trigger.DeadlineHit, ContestState.Voting);

            //State: TaskSuggestionCollection
            //Community suggests possible tasks for the next challenge
            _stateMachine.Configure(ContestState.TaskSuggestionCollection)
                .OnActivate(OnTaskSuggestionCollectionActivated)
                .OnEntry(EnteredTaskSuggestionCollection)
                .Permit(Trigger.DeadlineHit, ContestState.TaskSuggestionVoting);

            //State: TaskSuggestionVoting
            //Next round task voting in progress
            _stateMachine.Configure(ContestState.TaskSuggestionVoting)
                .OnActivate(OnTaskSuggestionVotingActivated)
                .OnEntry(EnteredTaskSuggestionVoting)
                .Permit(Trigger.DeadlineHit, ContestState.FinalizingNextRoundTaskPollVoting)
                .Permit(Trigger.NotEnoughContesters, ContestState.Standby);

            //State: FinalizingNextRoundTaskPollVoting
            //System chooses the winner among suggested tasks
            _stateMachine.Configure(ContestState.FinalizingNextRoundTaskPollVoting)
                .OnActivate(OnFinalizingNextRoundTaskPollVotingActivated)
                .OnEntry(EnteredFinalizingNextRoundTaskPollVoting)
                .Permit(Trigger.TaskSelectedByPoll, ContestState.Contest)
                .Permit(Trigger.NotEnoughContesters, ContestState.Standby)
                .Permit(Trigger.NotEnoughVotes, ContestState.Standby);

            //If not enough activity (entries or votes), turn off challenges
            _stateMachine.Configure(ContestState.ChoosingNextTask)
                .Permit(Trigger.NotEnoughContesters, ContestState.Standby);
            _stateMachine.Configure(ContestState.ChoosingNextTask)
                .Permit(Trigger.NotEnoughVotes, ContestState.Standby);
            _stateMachine.Configure(ContestState.Contest)
                .Permit(Trigger.NotEnoughContesters, ContestState.Standby);



            _stateMachine.OnTransitioned(transition =>
            {
                logger.Info($"Transitioned to {transition.Destination} state from {transition.Source}");                
            });

            _stateMachine.OnUnhandledTrigger((state, trigger) =>
            {
                logger.Warn($"Unhandled trigger : trigger {trigger} in state {state}");               
            });
        }

        /// <summary>
        /// Event is currently not used
        /// </summary>
        /// <param name="obj"></param>
        private void OnChatMigrationFailed(ChatMigrationFailedEvent obj)
        {
            logger.Info($"Putting bot to standby");
            _stateMachine.Fire(_explicitStateSwitchTrigger,ContestState.Standby);
        }

        private void OnContestActivated()
        {
            if(!_isActivating)
                return;

            logger.Info($"Reactivated in Contest state");
        }


        private void OnKickstartContest(KickstartContestEvent kickstartContestEvent)
        {
            _stateMachine.Fire(Trigger.TaskApproved);
        }

        private void _scheduler_PreviewDeadlineHit()
        {
            logger.Info($"State scheduler fired PreviewDeadlineHit");

            _stateMachine.Fire(Trigger.PreviewDeadlineHit);
        }

        private void _scheduler_DeadlineHit()
        {
            logger.Info($"State scheduler fired DeadlineHit");

            _stateMachine.Fire(Trigger.DeadlineHit);
        }

        private async void OnContestStartedOrResumed(StateMachine<ContestState, Trigger>.Transition arg)
        {
            await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false);

            try
            {
                if (arg.Trigger == Trigger.PreviewDeadlineHit)
                    await _contestController.WarnAboutContestDeadlineSoon();
                else if (arg.Trigger == Trigger.TaskApproved)
                    await _contestController.InitiateContestAsync();
            }
            catch (Exception e)
            {
                logger.Error($"Exception while starting contest - {e.Message}");
                await _broadcastController.SqueakToAdministrators(e.Message);
                _stateMachine.Fire(_explicitStateSwitchTrigger,ContestState.Standby);
            }
            finally
            {
                _transitionSemaphoreSlim.Release();
            }
        }

        private async void OnVotingStartedOrResumed(StateMachine<ContestState, Trigger>.Transition arg)
        {
            var activeEntries = _repository.GetActiveContestEntries().Count();

            await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false);

            try
            {
                if (arg.Trigger == Trigger.PreviewDeadlineHit)
                    await _votingController.WarnAboutVotingDeadlineSoon();
                else if (arg.Trigger == Trigger.DeadlineHit)
                {
                    if (activeEntries < _configuration.MinAllowedContestEntriesToStartVoting)
                    {
                        logger.Warn(
                            $"GetActiveContestEntries found no active entries, announcing and switching to standby");

                        await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughEntriesAnnouncement,
                            pin: true);

                        _stateMachine.Fire(Trigger.NotEnoughContesters);

                        return;
                    }

                    await _votingController.StartVotingAsync();
                }
            }
            finally
            {
                _transitionSemaphoreSlim.Release();
            }
        }
        
        private void ActivatedInnerCircleVoting()
        {
            if(!_isActivating)
                return;

            InnerCircleVotingAsync(true);
        }

        private async void InnerCircleVotingAsync(bool reactivated)
        {
            if(reactivated)
                logger.Warn($"Reactivated inner circle voting");

            bool verdict;
            var icvc = _innerCircleVoteGenerator();

            await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false);

            try
            {
                verdict = await icvc.PremoderateTaskForNewRound(reactivated);
            }
            finally
            {
                _transitionSemaphoreSlim.Release();
            }

            logger.Info($"Premoderation verdict: {verdict}");

            _stateMachine.Fire(verdict ? Trigger.TaskApproved : Trigger.TaskDeclined);
        }

        private void EnteredInnerCircleVoting(StateMachine<ContestState, Trigger>.Transition arg)
        {
            InnerCircleVotingAsync(false);
        }

        private void EnteredChoosingNextTask(StateMachine<ContestState, Trigger>.Transition arg)
        {
            ChooseNewTaskAsyncInternal(arg.Trigger== Trigger.TaskDeclined, false);
        }

        private void ActivatedChoosingNextTask()
        {
            if(!_isActivating)
                return;

            ChooseNewTaskAsyncInternal(false, true);
        }

        private async void ChooseNewTaskAsyncInternal(bool wasDeclined, bool isReactivation)
        {
            if(isReactivation)
                logger.Warn($"ChooseNewTaskAsyncInternal reactivated");

            var state = _repository.GetOrCreateCurrentState();

            if (null == state.CurrentWinnerId)
            {
                logger.Error("state.CurrentWinnerId == null unexpected");

                _repository.SetCurrentTask(SelectedTaskKind.Random, string.Empty);
                _stateMachine.Fire(Trigger.TaskSelectedByWinner);
                return;
            }

            var winner = _repository.GetExistingUserWithTgId(state.CurrentWinnerId.Value);

            if (winner?.ChatId == null)
            {
                logger.Error("_repository.GetUserWithTgId(state.CurrentWinnerId.Value)?.ChatId == null unexpected (winner user deleted?)");

                _repository.SetCurrentTask(SelectedTaskKind.Random, string.Empty);
                _stateMachine.Fire(Trigger.TaskSelectedByWinner);
                return;
            }

            var taskTuple = Tuple.Create(SelectedTaskKind.Random, string.Empty);

            await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false);

            try
            {
                if (winner.ChatId == null)
                {
                    logger.Error(
                        "No ChatId found for current winner (winner.ChatId == null), cant start next task negotiation");
                }

                if(isReactivation)
                    await _client.SendTextMessageAsync(winner.ChatId,
                        _loc.GeneralReactivationDueToErrorsMessage,
                        ParseMode.Html);

                if (wasDeclined)
                {
                    await _client.SendTextMessageAsync(winner.ChatId,
                        _loc.ChooseWiselyPrivateMessage,
                        ParseMode.Html);
                }
                else
                {
                    await _client.SendTextMessageAsync(winner.ChatId,
                        _loc.CongratsPrivateMessage,
                        ParseMode.Html);
                }

                taskTuple = await _taskSelectorGenerator().SelectTaskAsync(winner);
            }
            finally
            {
                _transitionSemaphoreSlim.Release();
            }

            _repository.SetCurrentTask(taskTuple.Item1, taskTuple.Item2);

            if (taskTuple.Item1 == SelectedTaskKind.Poll)
                _stateMachine.Fire(Trigger.InitiatedNextRoundTaskPoll);
            else
                _stateMachine.Fire(Trigger.TaskSelectedByWinner);
        }

        
        private async void OnFinalizingActivate()
        {
            if(!_isActivating)
                return;

            OnFinalizingRoundInternal();
        }

        
        private async void EnteredFinalizingVoting(StateMachine<ContestState, Trigger>.Transition arg)
        {
            OnFinalizingRoundInternal();
        }

        public async Task<bool> WaitForStateTransition()
        {
            if (!await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false))
                return false;

            _transitionSemaphoreSlim.Release();

            return true;
        }

        private async void OnFinalizingRoundInternal()
        {
            var result =
                Tuple.Create<VotingController.FinalizationResult, User>(VotingController.FinalizationResult.NotEnoughContesters,
                    null);

            await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false);

            try
            {
                result = await _votingController.FinalizeVoting();
            }
            finally
            {
                _transitionSemaphoreSlim.Release();
            }

            switch (result.Item1)
            {
                case VotingController.FinalizationResult.Ok:
                    _stateMachine.Fire(Trigger.WinnerChosen);
                    break;
                case VotingController.FinalizationResult.NotEnoughVotes:
                    _stateMachine.Fire(Trigger.NotEnoughVotes);
                    break;
                case VotingController.FinalizationResult.NotEnoughContesters:
                    _stateMachine.Fire(Trigger.NotEnoughContesters);
                    break;
                case VotingController.FinalizationResult.Halt:
                    _stateMachine.Fire(_explicitStateSwitchTrigger, ContestState.Standby);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        //Double-check to circumvent Stateless crippled OnActivate/OnEnter logic
        //and avoid OnActivate acting as 2x OnEnter in transition
        private bool _isActivating = true;

        public async void Start()
        {
            _isActivating = true;

            await _stateMachine.ActivateAsync();

            _isActivating = false;

            await _scheduler.Activate();
        }

        private void SetCurrentState(ContestState currentState)
        {
            _repository.UpdateState(s=>s.State, currentState);            
        }

        private ContestState GetCurrentState()
        {
            return _repository.GetOrCreateCurrentState().State;
        }

        private async void OnTaskSuggestionCollectionActivated()
        {
            throw new NotImplementedException();
        }

        private async void EnteredTaskSuggestionCollection(StateMachine<ContestState, Trigger>.Transition arg)
        {
            throw new NotImplementedException();
        }

        private async void OnTaskSuggestionVotingActivated()
        {
            throw new NotImplementedException();
        }

        private async void EnteredTaskSuggestionVoting(StateMachine<ContestState, Trigger>.Transition arg)
        {
            throw new NotImplementedException();
        }

        private async void OnFinalizingNextRoundTaskPollVotingActivated()
        {
            throw new NotImplementedException();
        }

        private async void EnteredFinalizingNextRoundTaskPollVoting(StateMachine<ContestState, Trigger>.Transition arg)
        {
            throw new NotImplementedException();
        }


        public void Dispose()
        {
            _scheduler.Stop();

            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        }
    }
}