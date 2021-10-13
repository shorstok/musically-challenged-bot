using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Logging;
using musicallychallenged.Services.Sync;
using musicallychallenged.Services.Sync.DTO;

namespace tests.Mockups
{
    public class MockIngestService : IPesnocloudIngestService
    {
        private readonly Lazy<SyncService> _syncServiceLazy;
        private static readonly ILog logger = Log.Get(typeof(MockIngestService));
        public bool MockIsAlive { get; set; } = true;

        public ConcurrentDictionary<long, BotTrackDescriptor> Tracks { get; } = new();
        public ConcurrentDictionary<long, BotRoundDescriptor> Rounds { get; } = new();

        private readonly SemaphoreSlim _semaphore = new(1, 1);
        
        public Task<bool> IsAlive(CancellationToken token) => Task.FromResult(MockIsAlive);

        private readonly SemaphoreSlim _checkpointSemaphore = new(1, 1);
        private TaskCompletionSource<Guid> _checkpointCompletionSource = new();
        private Guid _checkpointId = new();

        public MockIngestService(Lazy<SyncService> syncServiceLazy)
        {
            _syncServiceLazy = syncServiceLazy;
        }
        
        public async Task WaitTillQueueIngested(CancellationToken token)
        {
            await _checkpointSemaphore.WaitAsync(token);

            try
            {
                //This is a robust way to ensure ingest service queue was processed by sync chain.
                //We create a debug checkpoint event and wait till it reaches our service
                
                _checkpointCompletionSource = new TaskCompletionSource<Guid>();

                _checkpointId = Guid.NewGuid();

                _syncServiceLazy.Value.CreateCheckpoint(_checkpointId);
                logger.Info($"Created checkpoint {_checkpointId}");

                //Wait till we get to this checkpoint or timeout, what would be the first
                await using var registration = token.Register(() => _checkpointCompletionSource.TrySetCanceled());
                await _checkpointCompletionSource.Task;
            }
            finally
            {
                _checkpointSemaphore.Release();
            }
        }
        
        public Task AddOrUpdateTrack(TrackAddedOrUpdatedSyncEvent syncEvent, CancellationToken cancellationToken)
        {
            var track = new BotTrackDescriptor
            {
                Id = syncEvent.InternalEntryId,
                Description = syncEvent.Description,
                SubmissionDate = syncEvent.SubmissionDate,
                Title = syncEvent.PayloadTitle,
                Votes = syncEvent.Votes,
                RoundId = syncEvent.InternalRoundNumber,
                Source = "challenged",
                AuthorId = syncEvent.AuthorId?.ToString(),
            };
            
            Tracks.AddOrUpdate(syncEvent.InternalEntryId, id =>
            {
                logger.Info($"Ingested new track {id}");
                return track;
            }, (id, descriptor) =>
            {
                logger.Info($"Ingested updated track {id}");
                return track;
            });
            
            return Task.CompletedTask;
        }

        public Task StartOrUpdateRound(RoundStartedOrUpdatedSyncEvent syncEvent,
            CancellationToken cancellationToken)
        {
            var track = new BotRoundDescriptor
            {
                Id = syncEvent.InternalRoundNumber,
                Source = "challenged",
                State = syncEvent.RoundState,
                Title = syncEvent.RoundTitle,
                EndDate = syncEvent.EndDate,
                StartDate = syncEvent.StartDate,
                TaskText = syncEvent.Description,
            };
            
            Rounds.AddOrUpdate(syncEvent.InternalRoundNumber, id =>
            {
                logger.Info($"Ingested new round {id}");
                return track;
            }, (id, descriptor) =>
            {
                logger.Info($"Ingested updated round {id}");
                return track;
            });
            
            return Task.CompletedTask;
        }

        public async Task UpdateVotes(VotesUpdatedSyncEvent voteUpdatedSyncEvent, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                foreach (var (entryId, voteValue) in voteUpdatedSyncEvent.VotesPerEntries)
                {
                    if (!Tracks.TryGetValue(entryId, out var track))
                    {
                        logger.Warn($"UpdateVote, but track with id {entryId} not found in ingested");
                        return;
                    }

                    track.Votes = (decimal?)voteValue;
                    
                    logger.Info($"Updated vote = {voteValue} for track {entryId}");
                }
            }
            finally
            {
                _semaphore.Release();
            }
            

        }

        public async Task PatchTrack(TrackPatchSyncEvent patchSyncEvent, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                if (!Tracks.TryGetValue(patchSyncEvent.Payload.Id, out var track))
                {
                    logger.Warn($"PatchTrack, but track with id {patchSyncEvent.Payload.Id} not found in ingested");
                    return;
                }

                track.Votes = patchSyncEvent.Payload.Votes ?? track.Votes;
                track.RoundId = patchSyncEvent.Payload.RoundId ?? track.RoundId;
                track.Title = patchSyncEvent.Payload.Title ?? track.Title;
                track.Description = patchSyncEvent.Payload.Description ?? track.Description;
                track.SubmissionDate = patchSyncEvent.Payload.SubmissionDate ?? track.SubmissionDate;
                
                logger.Info($"Patched track {patchSyncEvent.Payload.Id}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task PatchRound(RoundPatchSyncEvent patchSyncEvent, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                if (!Rounds.TryGetValue(patchSyncEvent.Payload.Id, out var round))
                {
                    logger.Warn($"PatchRound, but round with id {patchSyncEvent.Payload.Id} not found in ingested");
                    return;
                }

                round.Title = patchSyncEvent.Payload.Title ?? round.Title;
                round.State = patchSyncEvent.Payload.State ?? round.State;
                round.TaskText = patchSyncEvent.Payload.TaskText ?? round.TaskText;
                round.EndDate = patchSyncEvent.Payload.EndDate ?? round.EndDate;
                round.StartDate = patchSyncEvent.Payload.StartDate ?? round.StartDate;
                
                logger.Info($"Patched round {patchSyncEvent.Payload.Id}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public Task DeleteTrack(TrackDeletedSyncEvent trackDeletedSync, CancellationToken cancellationToken)
        {
            if(!Tracks.TryRemove(trackDeletedSync.Id, out var _))
                logger.Warn($"DeleteTrack, but track with id {trackDeletedSync.Id} not found in ingested");
            
            return Task.CompletedTask;
        }

        public Task Checkpoint(DebugCheckpointSyncEvent debugCheckpoint, CancellationToken cancellationToken)
        {
            logger.Info($"Reached debug checkpoint {debugCheckpoint.Id}");

            if (_checkpointId == debugCheckpoint.Id)
            {
                logger.Info($"Matched awaited checkpoint {_checkpointId}");
                _checkpointCompletionSource.TrySetResult(debugCheckpoint.Id);
            }
            
            return Task.CompletedTask;
        }
    }
}