# What it is
A Telegram bot that runs recurring music-making challenges ("rounds"). A round starts with a task/prompt (e.g., style constraint, composition rule), participants submit audio tracks to the bot, after a deadline, the bot starts voting (typically in a separate voting channel/chat).

Votes are consolidated -> winner is announced -> the next task is selected (winner-driven or poll-driven).

The system repeats indefinitely.

# Domain model

Core objects
`SystemState`: single row of "what's happening right now" (current round number, current task text, bot state, deadlines, message ids to edit/pin, etc.). You can see it being used everywhere via `GetOrCreateCurrentState()` and `UpdateState(...)` (e.g., contest pin message id, deadline UTC, current round number).
`ContestState` (FSM): high-level lifecycle state machine (Standby -> Contest -> Voting -> ...). Scheduling and command availability often depend on this. 
`ActiveContestEntry`: a current-round submission (audio container message refs, author, description, per-entry vote aggregation).
`Vote`: `(UserId, ContestEntryId, Value, Timestamp)`; created/updated during voting; later consolidated.
`PostponeRequest`: used to extend deadlines by quorum ("3 users asked to postpone").
`RandomTask`: fallback task source if selection/poll fails. 
`SyncEvent`: an outbox/event log for remote Pesnocloud syncing (tracks/rounds/votes).

# Exact lifecycle flow (rounds / turns)
The lifecycle is managed by a finite state machine ('Stateless' nuget package) + a polling scheduler that emits "preview deadline hit" and "deadline hit" events.
## State machine overview (high-level)
Typical "happy path":
```
Standby
  -> Contest (submissions open)
    -> Voting (voting open)
      -> FinalizingVotingRound (compute winner)
        -> ChoosingNextTask (winner chooses OR poll is prepared)
          -> InnerCircleVoting (optional moderation)
            -> Contest (next round starts)
```
Alternative path for next task via poll:
```
Standby
  -> TaskSuggestionCollection (collect suggestions)
    -> TaskSuggestionVoting (vote on suggestions)
      -> FinalizingNextRoundTaskPollVoting (pick suggestion)
        -> Contest
```
The exact transitions are driven by triggers like `PreviewDeadlineHit`, `DeadlineHit`, "not enough votes", "winner chosen", "task selected", etc. 

## How "deadlines" and "pre-deadline reminders" actually happen

The scheduler (polling-based)
`PollingStateScheduler` continuously:
- Reads `SystemState` from the repository.
- Looks at `state.NextDeadlineUTC` and `state.State`.
- Computes preview time = deadline - preDeadlineDuration depending on state.
- Emits `PreviewDeadlineHit` once, `DeadlineHit` once
- Resets preview/final "signaled flags" if state changes, or deadline shifts (postpone).

Pre-deadline durations are configurable per state:
- Contest preview hours
- Voting preview hours
- TaskSuggestionCollection preview hours

TimeService sets the deadlines. Deadlines are stored as `NextDeadlineUTC` and set via `TimeService`, which:
- Can schedule "N days at HH:00" for contest rounds (typical),
- Or schedule "N hours from now" for short phases (voting/polls),
- Provides "time left" formatting in the configured announcement timezone.
## Contest (submissions phase)
### Starting a contest `ContestController.InitiateContestAsync()`:
- Schedules contest deadline (default fallback is `ContestDurationDays ?? 14` at 22:00).
- Announces/pins the task in main channel and announces in voting channel.
- Stores the pinned message id into `SystemState.CurrentTaskMessagelId`.
- Closes previously open postpone requests.
- Creates a new round in Pesnocloud via SyncService (`CreateRound(...)`).
### Submitting an entry
User sends audio to the bot (via direct message).
The bot:
- Validates state is contest, author is eligible, etc.
- Forwards the audio to the configured voting channel (so everyone can listen).
- Creates/updates an `ActiveContestEntry` (and stores message/container ids).
 - Queues a Pesnocloud `TrackAddedOrUpdatedSyncEvent` through `SyncService.AddOrUpdateEntry(...)`.
### Deadline preview / deadline hit in Contest
Preview: the bot can warn about the contest deadline soon.
Deadline: the FSM moves into `Voting` (or `Standby` if not enough contestants). The scheduler is the thing that causes the trigger.
## Voting (voting phase)
Voting mechanics
`VotingController` inherits `VotingControllerBase<ActiveContestEntry, Vote>`. Important detail: on a user's first vote, it may pre-create default votes for all* active entries except the one they voted on (so missing votes aren't "null").
#### Updating current task/voting message
`VotingController.UpdateCurrentTaskMessage()` edits the pinned voting deadline message in main channel (stored in `SystemState.CurrentVotingDeadlineMessageId`). 
#### Voting deadline preview / deadline hit
Preview: optional "voting about to end" announcements.
Deadline: transition into `FinalizingVotingRound`.

## Finalizing voting -> choosing winner -> next task
Finalization is handled as a distinct FSM state to ensure: consolidation happens once, winner selection is consistent, bot can recover/retry cleanly.
Votes are consolidated via repository methods like `ConsolidateVotesForActiveEntriesGetAffected()` (also used to isolate previous round tasks / increment round number).

Winner selection then drives task selection:
- Winner chooses (manual) or
- Bot kicks off a suggestion poll (below) or
- Falls back to random tasks if needed.
## Next round task poll (TaskSuggestion* states)
This is explicitly kickstartable from Standby.
Kickstart command
`KickstartNextRoundTaskPollCommandHandler`:
Allowed only in `Standby`.
Requires confirmation (inline buttons YES/NO).
Calls `NextRoundTaskPollController.KickstartTaskPollAsync(user)`. 
Collection -> voting
The scheduler also supports preview/deadline events for `TaskSuggestionCollection` with its own preview duration config.

# Key technological decisions
## Architecture style: "controllers + state machine + repository"
`Controllers` = domain workflows (ContestController, VotingController, PostponeService, NextRoundTaskPollController, etc.).
`StateController` = orchestration of those workflows via FSM triggers.
`Repository` = persistence abstraction for SQLite (with an in-memory variant for tests).
Event aggregator = internal pub/sub for cross-cutting events (message deleted, bot blocked, kickstart contest, etc.). ContestController subscribes to message deletion and bot-block events, for example. 
This keeps "what happens" (controllers) separate from "when/why it happens" (StateController + scheduler).

## Persistence: SQLite + repository methods tuned to the domain
Storage is centralized behind `IRepository` and implemented in SQLite with explicit SQL for critical paths:
Vote creation and mass-default-vote insert is raw SQL with transactions.
Postpone requests and quorum tracking are persisted and aggregated in SQL.
## Scheduling: polling loop rather than cron/jobs
Scheduling is "pull current state from DB every N ms" (`DeadlinePollingPeriodMs`), emit preview/deadline signals once per deadline, reset signals when deadline shifts (postpone) or state changes.

## Postpone system: quorum-based deadline shifting
Postpone is intentionally constrained:
- Only available in Contest state.
- Only allowed to users with at least one finished entry.
- Only one open request per user.

When quorum is reached, it selects the largest postpone request as the one that actually extends the deadline and closes the rest as discarded.
After postponing it updates both contest and voting task messages and announces the new deadline.

## Pesnocloud remote feature: outbox + poller + payload extraction + ffmpeg "conformer"

The pattern: "outbox table" (SyncEvent) + reliable poller
Instead of calling Pesnocloud inline during user operations, the bot:
- Creates a `SyncEvent` row (serialized DTO).
- A background loop (`SyncService`) polls unsynced events every `PesnocloudPollingPeriodMs`.
- It checks remote health (`/bot/isHealthy`).
- Processes events and marks them synced.
- Uses exponential backoff on failures.

Audio upload pipeline
`TelegramPayloadExtractor` downloads the audio file from Telegram into `AppData/temp-payloads`.
`PesnocloudConformer` converts audio to mp3 (libmp3lame, 192k) via configured `FfmpegPath`.
`PesnocloudIngestService` uploads the track to `PUT /bot/track` with metadata in query params and (important) uses a base64 "X-Entry-Description" header to avoid URL length limits.

### Round and votes sync (pesnocloud)
Rounds are created/updated via `PUT /bot/round` and patched via `PATCH /bot/round`.
Votes are pushed as a snapshot to `PUT /bot/votes`.

# "Midvote" feature: controlled late submissions
`MidvoteEntryController` implements a niche feature:
- Maintains a set of "pins" (strings) that enable mid-vote submissions.
- Ensures only one midvote submission per author if they already have an active entry.
- Uses a semaphore to serialize message handling (avoid race conditions).

This is a deliberate "escape hatch" for special rounds/events.

# Services & where to find them in code
## Core orchestration
`Services/StateController.cs` -- FSM orchestration (drives state transitions based on scheduler + triggers). 
`Services/IStateScheduler.cs` -- scheduler interface (PreviewDeadlineHit / DeadlineHit). 
`Services/PollingStateScheduler.cs` -- DB-driven polling scheduler; reads `NextDeadlineUTC`, computes preview instants, handles "deadline moved" resets. 
## Round lifecycle controllers
`Services/ContestController.cs` -- contest start, pinning, deadline announcements, submissions, message deleted/bot blocked handling; calls SyncService for round/entry sync.
`Services/VotingController.cs` -- voting UI + vote persistence + deadline message updates; callback prefix `v`. 
`Services/PostponeService.cs` -- quorum postpone logic; updates `NextDeadlineUTC` and announces changes.
`Services/RandomTaskRepository.cs` -- random fallback tasks. 
`Services/MidvoteEntryController.cs` -- controlled "mid-vote submission" feature. 
## Telegram routing layer
`Services/Telegram/CommandManager.cs` -- matches `/commands`, checks permissions, dispatches handlers. 
`Services/Telegram/DialogManager.cs` -- manages interactive flows (waiting for user text/callback).
 `Services/Telegram/` -- wrapper around Telegram client calls, throttling, error handling.
## Sync / Pesnocloud
`Services/Sync/SyncService.cs` -- poller + dispatcher for sync events (outbox pattern).
`Services/Sync/PesnocloudIngestService.cs` -- actual HTTP calls to Pesnocloud (`/bot/isHealthy`, `/bot/track`, `/bot/round`, `/bot/votes`).
`Services/Sync/PesnocloudConformer.cs` -- ffmpeg mp3 conversion pipeline.
`Services/Sync/TelegramPayloadExtractor.cs` -- downloads Telegram audio locally.
 `Services/Sync/DTO/` -- payload contracts (`BotRoundDescriptor`, `BotTrackDescriptor`, `BotVotesSnapshot`, `SyncEventDto` etc.).
## Commands & how they map to the flow
Examples (not exhaustive, but the important "flow shapers"):
`Commands/PostponeCommandHandler.cs` -- user postpone requests (contest only); uses inline keyboard option selection.
`Commands/RemindCommandHandler.cs` -- supervisor reminder: forces warning + message updates. 
`Commands/SetDeadlineTimeToCommandHandler.cs` -- supervisor override of deadline; requires confirmation; uses announcement timezone.
`Commands/KickstartNextRoundTaskPollCommandHandler.cs` -- supervisor kickstart task suggestion poll from Standby. 
Command names are centrally defined in `Commands/Schema.cs`. 

# Configuration: what matters & where

Configuration is loaded from `service-config.json` (via `BotConfiguration.LoadOrCreate(...)`) and supports environment overrides with `Config__{JsonPropertyName}`. It includes:
preview hours per phase,
postpone quorum and options,
min/max vote values,
Pesnocloud base uri + bot token,
polling periods,
ffmpeg path,
deployment chat ids (main/voting).

# Practical "where do I change X?" cheatsheet
## Change contest/voting deadline behavior
Preview lead times: `BotConfiguration.DeadlineEventPreviewTimeHours` 
Polling interval: `BotConfiguration.DeadlinePollingPeriodMs` 
Scheduler logic: `PollingStateScheduler` (preview instant, resetting on deadline shift) 
Deadline set/edit: `TimeService` 
## Change voting scale / UI
`Services/VotingController.cs` (emojis, prefix, default vote logic) 
## Change postpone rules
`BotConfiguration.PostponeQuorum`, `PostponeHoursAllowed`, `PostponeOptions` 
Enforcement and deadline shifting: `Services/PostponeService.cs` 
## Change Pesnocloud behavior
Poll rate: `PesnocloudPollingPeriodMs` 
Health check + endpoints: `PesnocloudIngestService`
Audio conversion: `PesnocloudConformer` (+ `FfmpegPath`)
Event queueing points: `SyncService.AddOrUpdateEntry`, `CreateRound`, `UpdateVotes`, `PatchRoundInfo`
