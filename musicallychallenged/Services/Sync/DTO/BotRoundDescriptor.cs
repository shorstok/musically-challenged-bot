using System;
using System.Text.Json.Serialization;

namespace musicallychallenged.Services.Sync.DTO
{
    public class BotRoundDescriptor
    {
        [JsonPropertyName("Id")] public long Id { get; set; }
        [JsonPropertyName("Source")] public string Source { get; set; } 
        
        [JsonPropertyName("Title")] public string Title { get; set; }

        [JsonPropertyName("StartDate")] public DateTime? StartDate { get; set; }
        [JsonPropertyName("EndDate")] public DateTime? EndDate { get; set; }
        
        [JsonPropertyName("TaskText")] public string TaskText { get; set; }
        [JsonPropertyName("State")] public BotContestRoundState? State { get; set; }
    }
}