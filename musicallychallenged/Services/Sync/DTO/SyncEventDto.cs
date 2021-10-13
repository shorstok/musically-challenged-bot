using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NodaTime;

namespace musicallychallenged.Services.Sync.DTO
{
    public enum BotContestRoundState
    {
        Open,
        Voting,
        Closed
    }
    
    public abstract class SyncEventDto
    {
    }
    
    public class DebugCheckpointSyncEvent : SyncEventDto
    {
        [JsonProperty("id")] public Guid Id { get; set; }
    }    

    public class TrackPatchSyncEvent : SyncEventDto
    {
        [JsonProperty("payload")] public BotTrackDescriptor Payload { get; set; }
    }
    
    public class RoundPatchSyncEvent : SyncEventDto
    {
        [JsonProperty("payload")] public BotRoundDescriptor Payload { get; set; }
    }
    
    
    public class TrackDeletedSyncEvent : SyncEventDto
    {
        [JsonProperty("id")] public int Id { get; set; }
    }

    //Created on: Add or update new submission
    public class TrackAddedOrUpdatedSyncEvent : SyncEventDto
    {
        [JsonProperty("id")] public int InternalEntryId { get; set; }

        [JsonProperty("roundNumber")] public int InternalRoundNumber { get; set; }

        [JsonProperty("description")] public string Description { get; set; }

        [JsonProperty("payload")] public string PayloadPath { get; set; }

        [JsonProperty("author")] public string Author { get; set; }
        [JsonProperty("authorId")] public long? AuthorId { get; set; }
        [JsonProperty("title")] public string PayloadTitle { get; set; }
        [JsonProperty("votes")] public int? Votes { get; set; }
        
        [JsonProperty("submitted")] 
        public DateTime SubmissionDate { get; set; }
    }

    //Created on: Add or update vote
    public class VotesUpdatedSyncEvent : SyncEventDto
    {
        [JsonProperty("votes")] public Dictionary<int, int> VotesPerEntries { get; set; } = new();
    }

    //Created on: new round started / round state changed
    public class RoundStartedOrUpdatedSyncEvent : SyncEventDto
    {
        [JsonProperty("round")] public int InternalRoundNumber { get; set; }

        [JsonProperty("description")] public string Description { get; set; }

        [JsonProperty("startDateTime")] public DateTime? StartDate { get; set; }

        [JsonProperty("endDateTime")] public DateTime? EndDate { get; set; }
        [JsonProperty("title")] public string RoundTitle { get; set; }
        [JsonProperty("state")] public BotContestRoundState RoundState { get; set; }
    }

}