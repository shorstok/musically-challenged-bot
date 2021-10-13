using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace musicallychallenged.Services.Sync.DTO
{
    public class BotVotesSnapshot
    {
        [JsonPropertyName("votes")] public Dictionary<string, int> Votes { get; set; } = new();
    }
}