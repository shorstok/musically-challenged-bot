using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Logging;
using musicallychallenged.Services.Sync.DTO;
using Newtonsoft.Json;
using NodaTime;
using Telegram.Bot.Types;

namespace musicallychallenged.Services.Sync
{
    public class SyncService : IStartable, IDisposable
    {
        private readonly IRepository _repository;
        private readonly PayloadExtractor _extractor;
        private readonly IBotConfiguration _botConfiguration;
        private readonly IPesnocloudIngestService _ingestService;
        private readonly IClock _clock;

        private static readonly ILog logger = Log.Get(typeof(SyncService));

        private Task _syncPollerTask = Task.CompletedTask;
        private readonly CancellationTokenSource _cancellation = new();

        public SyncService(IRepository repository, PayloadExtractor extractor,
            IBotConfiguration botConfiguration,
            IPesnocloudIngestService ingestService, IClock clock)
        {
            _repository = repository;
            _extractor = extractor;
            _botConfiguration = botConfiguration;
            _ingestService = ingestService;
            _clock = clock;
        }

        public void Start()
        {
            logger.Info($"Starting");

            //Fire the sync loop
            _syncPollerTask = SyncPoller(_cancellation.Token);
        }

        /// <summary>
        ///     Main sync polling loop 
        /// </summary>
        private async Task SyncPoller(CancellationToken cancellationToken)
        {
            bool? isAlive = null;
            int consecutiveSyncErrors = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_botConfiguration.PesnocloudPollingPeriodMs, cancellationToken);

                var syncEvents = _repository.GetSyncEvents(onlyUnsynced: true).ToArray();

                if(!syncEvents.Any())
                    continue;
                
                if (!await _ingestService.IsAlive(cancellationToken))
                {
                    if (isAlive!=false)
                        logger.Warn($"Pesnocloud serivce is down...");
                    isAlive = false;
                    continue;
                }

                if (isAlive!=true)
                    logger.Info($"Pesnocloud serivce is up");

                isAlive = true;
                
                logger.Info($"Going to process {syncEvents.Length} sync event(s)");

                foreach (var syncEvent in syncEvents)
                {
                    logger.Info($"Syncing event {syncEvent.Id} @ {syncEvent.CreatedAt}...");

                    try
                    {
                        await ProcessSyncEvent(syncEvent, cancellationToken);
                        _repository.MarkSynced(syncEvent);

                        logger.Info($"Synced event {syncEvent.Id}/{syncEvent.CreatedAt} OK!");
                    }
                    catch (Exception e)
                    {
                        var millisecondsDelay = 1000 + 1000 * (int)Math.Pow(2, consecutiveSyncErrors);
                        
                        if(consecutiveSyncErrors==0)
                            logger.Error($"Failed syncing event {syncEvent.Id}: {e.GetType().Name}/{e.Message}, " +
                                         $"not marking as synced, next attempt in {millisecondsDelay}ms");
                        else
                            logger.Error(
                                $"Failed syncing event {syncEvent.Id}: `{e}`; " +
                                $"not marking as synced, next attempt in {millisecondsDelay}ms");



                        await Task.Delay(millisecondsDelay, cancellationToken);
                        
                        //Don't grow beyond 30 minutes
                        if (millisecondsDelay < 30 * 60 * 1000)
                            consecutiveSyncErrors++;
                        
                        continue;
                    }
                }
            }
        }

        private async Task ProcessSyncEvent(SyncEvent syncEvent, CancellationToken cancellationToken)
        {
            logger.Info($"Processing sync event#{syncEvent?.Id.ToString() ?? "null!"}");

            if (null == syncEvent)
                throw new ArgumentNullException(nameof(syncEvent));

            var payload = JsonConvert.DeserializeObject<SyncEventDto>(syncEvent.SerializedDto, JsonFormatters.Compact);

            switch (payload)
            {
                case TrackDeletedSyncEvent trackDeletedSync:
                    await _ingestService.DeleteTrack(trackDeletedSync, cancellationToken);
                    break;
                case RoundPatchSyncEvent patchSyncEvent:
                    await _ingestService.PatchRound(patchSyncEvent, cancellationToken);
                    break;
                case TrackPatchSyncEvent patchSyncEvent:
                    await _ingestService.PatchTrack(patchSyncEvent, cancellationToken);
                    break;
                case RoundStartedOrUpdatedSyncEvent roundStartedSyncEvent:
                    await _ingestService.StartOrUpdateRound(roundStartedSyncEvent, cancellationToken);
                    break;
                case TrackAddedOrUpdatedSyncEvent trackAddedSyncEvent:
                    await _ingestService.AddOrUpdateTrack(trackAddedSyncEvent, cancellationToken);
                    await _extractor.DisposePayload(trackAddedSyncEvent.PayloadPath, cancellationToken);
                    break;
                case VotesUpdatedSyncEvent voteUpdatedSyncEvent:
                    await _ingestService.UpdateVotes(voteUpdatedSyncEvent, cancellationToken);
                    break;
                case DebugCheckpointSyncEvent debugCheckpoint:
                    await _ingestService.Checkpoint(debugCheckpoint, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(syncEvent));
            }
        }

        public async Task AddOrUpdateEntry(Message container, ActiveContestEntry activeContestEntry)
        {
            var payloadPath = await _extractor.ExtractPayloadToFile(container, _cancellation.Token);
            var author = _repository.GetExistingUserWithTgId(activeContestEntry.AuthorUserId);

            var eventId = AddEvent(new TrackAddedOrUpdatedSyncEvent
            {
                Votes = activeContestEntry.ConsolidatedVoteCount,
                InternalEntryId = activeContestEntry.Id,
                InternalRoundNumber = activeContestEntry.ChallengeRoundNumber,
                Description = activeContestEntry.Description,
                Author = author?.GetUsernameOrNameWithCircumflex(),
                AuthorId = author?.Id,
                SubmissionDate = activeContestEntry.Timestamp.ToDateTimeUtc(),
                PayloadPath = payloadPath,
                PayloadTitle = container?.Audio?.Title ?? container?.Audio?.FileName
            });
        }


        public Task UpdateEntryDescription(int entryId, string entryDescription)
        {
            var eventId = AddEvent(new TrackPatchSyncEvent
            {
                Payload = new BotTrackDescriptor
                {
                    Id = entryId,
                    Description = entryDescription
                }
            });

            return Task.CompletedTask;
        }

        public Task UpdateRoundState(int roundNumber, BotContestRoundState state) =>
            PatchRoundInfo(roundNumber, null, null, state);

        public Task PatchRoundInfo(int roundNumber, string text, Instant? deadlineUtc, BotContestRoundState? state)
        {
            AddEvent(new RoundPatchSyncEvent
            {
                Payload = new BotRoundDescriptor
                {
                    Id = roundNumber,
                    TaskText = text,
                    EndDate = deadlineUtc?.ToDateTimeUtc(),
                    State = state
                }
            });

            return Task.CompletedTask;
        }

        public Task CreateRound(int roundNumber, string text, Instant? deadlineUtc)
        {
            AddEvent(new RoundStartedOrUpdatedSyncEvent
            {
                EndDate = deadlineUtc?.ToDateTimeUtc(),
                Description = text,
                InternalRoundNumber = roundNumber,
                StartDate = _clock.GetCurrentInstant().ToDateTimeUtc(),
                RoundState = BotContestRoundState.Open,
                RoundTitle = $"#Challenged{roundNumber}",
            });

            return Task.CompletedTask;
        }

        public void DeleteContestEntry(int deletedEntryId)
        {
            AddEvent(new TrackDeletedSyncEvent
            {
                Id = deletedEntryId
            });
        }

        public Task UpdateVotes(IEnumerable<ActiveContestEntry> activeEntries)
        {
            AddEvent(new VotesUpdatedSyncEvent
            {
                VotesPerEntries =
                    activeEntries.ToDictionary(entry => entry.Id, entry => entry.ConsolidatedVoteCount ?? 0)
            });

            return Task.CompletedTask;
        }

        private long AddEvent(SyncEventDto dto)
        {
            var id = _repository.CreateSyncEvent(
                JsonConvert.SerializeObject(dto, typeof(SyncEventDto), JsonFormatters.Compact));

            logger.Info($"Created sync evt {dto.GetType().Name}#{id}");

            return id;
        }

        public void CreateCheckpoint(Guid id) => AddEvent(new DebugCheckpointSyncEvent { Id = id });

        public void Dispose()
        {
            _cancellation.Cancel();
        }
    }
}