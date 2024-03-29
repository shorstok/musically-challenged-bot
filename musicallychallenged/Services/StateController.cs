﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Events;
using musicallychallenged.Services.Sync;
using musicallychallenged.Services.Sync.DTO;
using musicallychallenged.Services.Telegram;
using Stateless;
using Telegram.Bot.Types.Enums;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public sealed class StateController : IStartable, IDisposable
    {
        private static readonly ILog logger = Log.Get(typeof(StateController));

        private readonly IRepository _repository;
        private readonly SyncService _syncService;
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
        private readonly NextRoundTaskPollController _nextRoundTaskPollController;
        private readonly NextRoundTaskPollVotingController _nextRoundTaskPollVotingController;

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
            TaskSelectedByFallthrough,
        }

        private SemaphoreSlim _transitionSemaphoreSlim = new SemaphoreSlim(1, 1);
        private const int transitionMaxWaitMs = 60*60*1000;

        private readonly StateMachine<ContestState, Trigger> _stateMachine;

        private ISubscription[] _subscriptions;

        private readonly StateMachine<ContestState, Trigger>.TriggerWithParameters<ContestState>
            _explicitStateSwitchTrigger;

        public StateController(IRepository repository,
            SyncService syncService,
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
            VotingController votingController,
            NextRoundTaskPollController nextRoundTaskPollController,
            NextRoundTaskPollVotingController nextRoundTaskPollVotingController)
        {
            _repository = repository;
            _syncService = syncService;
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
            _nextRoundTaskPollController = nextRoundTaskPollController;
            _nextRoundTaskPollVotingController = nextRoundTaskPollVotingController;

            _scheduler.DeadlineHit += _scheduler_DeadlineHit;
            _scheduler.PreviewDeadlineHit += _scheduler_PreviewDeadlineHit;

            _subscriptions = new ISubscription[]
            {
                _eventAggregator.Subscribe<KickstartContestEvent>(OnKickstartContest),
                _eventAggregator.Subscribe<ChatMigrationFailedEvent>(OnChatMigrationFailed),
                _eventAggregator.Subscribe<KickstartNextRoundTaskPollEvent>(OnKickstartNextRoundTaskPoll),
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
            _stateMachine.Configure(ContestState.Standby)
                .OnEntry(OnStandbyEnteredOrResumed)
                .PermitDynamic(_explicitStateSwitchTrigger, state => state);
            _stateMachine.Configure(ContestState.Standby).Permit(Trigger.TaskApproved, ContestState.Contest);
            _stateMachine.Configure(ContestState.Standby).Permit(Trigger.InitiatedNextRoundTaskPoll, ContestState.TaskSuggestionCollection);

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
                .OnEntry(EnteredOrResumedTaskSuggestionCollection)
                //check - whether to extend collection phase
                .PermitReentry(Trigger.PreviewDeadlineHit)
                .Permit(Trigger.NotEnoughContesters, ContestState.Standby)
                .Permit(Trigger.DeadlineHit, ContestState.TaskSuggestionVoting);

            //State: TaskSuggestionVoting
            //Next round task voting in progress
            _stateMachine.Configure(ContestState.TaskSuggestionVoting)
                .OnActivate(OnTaskSuggestionVotingActivated)
                .OnEntry(EnteredTaskSuggestionVoting)
                .Permit(Trigger.DeadlineHit, ContestState.FinalizingNextRoundTaskPollVoting)
                .Permit(Trigger.TaskSelectedByFallthrough, ContestState.Contest)
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

        private void OnKickstartNextRoundTaskPoll(KickstartNextRoundTaskPollEvent kickstartNextRoundTaskPollEvent)
        {
            _stateMachine.Fire(Trigger.InitiatedNextRoundTaskPoll);
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
                else
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

        private async void OnStandbyEnteredOrResumed(StateMachine<ContestState, Trigger>.Transition arg)
        {
            if(arg.Source == ContestState.Standby)
                return;
            
            await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false);

            try
            {
                var state = _repository.GetOrCreateCurrentState();

                if (arg.Source is ContestState.Voting or ContestState.Contest)
                {
                    logger.Info($"Syncing round {state.CurrentChallengeRoundNumber} as Closed");
                    await _syncService.UpdateRoundState(state.CurrentChallengeRoundNumber, BotContestRoundState.Closed);
                }
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
                            $"GetActiveContestEntries found less active entries than {_configuration.MinAllowedContestEntriesToStartVoting}, announcing and switching to standby");

                        await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughEntriesAnnouncement,
                            pin: true);

                        // closing this rounds' entries
                        _repository.ConsolidateVotesForActiveEntriesGetAffected();

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
                logger.Error("state.CurrentWinnerId == null unexpected, falling back to NextRoundTaskPoll");

                _repository.SetCurrentTask(SelectedTaskKind.Poll, string.Empty);
                _stateMachine.Fire(Trigger.InitiatedNextRoundTaskPoll);
                return;
            }

            var winner = _repository.GetExistingUserWithTgId(state.CurrentWinnerId.Value);

            if (winner?.ChatId == null)
            {
                logger.Error("_repository.GetUserWithTgId(state.CurrentWinnerId.Value)?.ChatId == null unexpected (winner user deleted?) falling back to NextRoundTaskPoll");

                _repository.SetCurrentTask(SelectedTaskKind.Poll, string.Empty);
                _stateMachine.Fire(Trigger.InitiatedNextRoundTaskPoll);
                return;
            }

            var taskTuple = Tuple.Create(SelectedTaskKind.Poll, string.Empty);

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

        
        private void OnFinalizingActivate()
        {
            if(!_isActivating)
                return;

            OnFinalizingRoundInternal();
        }

        
        private void EnteredFinalizingVoting(StateMachine<ContestState, Trigger>.Transition arg)
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
                Tuple.Create<VotingFinalizationResult, User>(VotingFinalizationResult.NotEnoughContesters,
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

            logger.Info($"Finished voting finalization. Result: {result.Item1}");

            switch (result.Item1)
            {
                case VotingFinalizationResult.Ok:
                    _stateMachine.Fire(Trigger.WinnerChosen);
                    break;
                case VotingFinalizationResult.NotEnoughVotes:
                    _stateMachine.Fire(Trigger.NotEnoughVotes);
                    break;
                case VotingFinalizationResult.NotEnoughContesters:
                    _stateMachine.Fire(Trigger.NotEnoughContesters);
                    break;
                case VotingFinalizationResult.Halt:
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

        private void OnTaskSuggestionCollectionActivated()
        {
            if (!_isActivating)
                return;

            logger.Info($"Reactivated in TaskSuggestionCollection state");
        }

        private async void EnteredOrResumedTaskSuggestionCollection(StateMachine<ContestState, Trigger>.Transition arg)
        {
            await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false);

            try
            {
                if (arg.Trigger == Trigger.PreviewDeadlineHit)
                {
                    var action = await _nextRoundTaskPollController.MaybeExtendCollectionPhase();
                    
                    if(action == NextRoundTaskPollController.ExtendAction.Standby)
                        _stateMachine.Fire(_explicitStateSwitchTrigger, ContestState.Standby);
                }
                else
                {
                    await _nextRoundTaskPollController.StartTaskPollAsync();
                }
            }
            catch (Exception e)
            {
                logger.Error($"Exception while starting contest - {e.Message}");
                await _broadcastController.SqueakToAdministrators(e.Message);
                _stateMachine.Fire(_explicitStateSwitchTrigger, ContestState.Standby);
            }
            finally
            {
                _transitionSemaphoreSlim.Release();
            }
        }

        private void OnTaskSuggestionVotingActivated()
        {
            if (!_isActivating)
                return;

            logger.Info($"Reactivated in TaskSuggestionVoting state");
        }

        private async void EnteredTaskSuggestionVoting(StateMachine<ContestState, Trigger>.Transition arg)
        {
            var activeSuggestion = _repository.GetActiveTaskSuggestions().ToArray();

            await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false);

            try
            {
                if (!activeSuggestion.Any())
                {
                    logger.Warn(
                        $"GetActiveTaskSuggestion found 0 active suggestions; announcing and switching to standby");

                    _nextRoundTaskPollController.HaltTaskPoll();

                    var announcement = await _broadcastController.AnnounceInMainChannel(_loc.NotEnoughSuggestionsAnnouncement,
                        pin: true);

                    if (announcement == null)
                        logger.Warn("Failed to announce transition to standby in the main channel");

                    _stateMachine.Fire(Trigger.NotEnoughContesters);
                    return;
                }
                else if (activeSuggestion.Length == 1)
                {
                    //Special case - only one task suggested.
                    //In this case, we skipp voting phase and using this one as a winner

                    logger.Info($"Task Collection phase yielded only single entry, so no sense in full voting - " +
                                $"executing fallthrough now and finalizing fake voting already");

                    var result = await _nextRoundTaskPollVotingController.FinalizeVoting();

                    if (result.Item1 != VotingFinalizationResult.Ok)
                    {
                        logger.Warn($"Something went wrong with fallthrough voting, expected Ok but got {result.Item1}. " +
                                    $"Switching to Standby just in case");
                        
                        _stateMachine.Fire(_explicitStateSwitchTrigger, ContestState.Standby);
                        return;
                    }

                    //This should switch us to Context state
                    _stateMachine.Fire(Trigger.TaskSelectedByFallthrough);

                    return;
                }
                else
                {
                    //We have task suggestions and we have to vote for them.
                    //This is general case

                    await _nextRoundTaskPollVotingController.StartVotingAsync();
                }


            }
            finally
            {
                _transitionSemaphoreSlim.Release();
            }
        }

        private void OnFinalizingNextRoundTaskPollVotingActivated()
        {
            if (!_isActivating)
                return;

            logger.Info($"Reactivated in FinalizingNextRoundTaskPollVoting state");
        }

        private async void EnteredFinalizingNextRoundTaskPollVoting(StateMachine<ContestState, Trigger>.Transition arg)
        {
            var result =
                Tuple.Create<VotingFinalizationResult, User>(
                    VotingFinalizationResult.NotEnoughContesters, null);

            await _transitionSemaphoreSlim.WaitAsync(transitionMaxWaitMs).ConfigureAwait(false);

            try
            {
                result = await _nextRoundTaskPollVotingController.FinalizeVoting();
            }
            finally
            {
                _transitionSemaphoreSlim.Release();
            }

            switch (result.Item1)
            {
                case VotingFinalizationResult.Ok:
                    _stateMachine.Fire(Trigger.TaskSelectedByPoll);
                    break;
                case VotingFinalizationResult.NotEnoughVotes:
                    _stateMachine.Fire(Trigger.NotEnoughVotes);
                    break;
                case VotingFinalizationResult.NotEnoughContesters:
                    _stateMachine.Fire(Trigger.NotEnoughContesters);
                    break;
                case VotingFinalizationResult.Halt:
                    _stateMachine.Fire(_explicitStateSwitchTrigger, ContestState.Standby);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

       
        /// <summary>
        /// Awaits till state change transitions are finished. Returns false on timeout
        /// </summary>
        public async Task<bool> YieldTransitionComplete(CancellationToken token)
        {
            try
            {
                await _transitionSemaphoreSlim.WaitAsync(token);

                try
                {
                    return true;
                }

                finally
                {
                    _transitionSemaphoreSlim.Release();
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }


        public void Dispose()
        {
            _scheduler.Stop();

            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        }
    }
}