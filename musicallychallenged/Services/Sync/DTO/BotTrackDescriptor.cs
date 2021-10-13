using System;
using System.Text.Json.Serialization;

namespace musicallychallenged.Services.Sync.DTO
{
    public class BotTrackDescriptor
    {
        [JsonPropertyName("Id")] public long Id { get; set; }
        [JsonPropertyName("RoundId")] public long? RoundId { get; set; }
        [JsonPropertyName("Source")] public string Source { get; set; }

        [JsonPropertyName("AuthorId")] public string AuthorId { get; set; }

        [JsonPropertyName("Title")] public string Title { get; set; }
        [JsonPropertyName("Description")] public string Description { get; set; }
        
        [JsonPropertyName("SubmissionDate")] public DateTime? SubmissionDate { get; set; }

        [JsonPropertyName("Votes")] public decimal? Votes { get; set; }
    }
}