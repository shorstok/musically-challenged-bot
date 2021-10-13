using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Microsoft.AspNetCore.WebUtilities;
using musicallychallenged.Config;
using musicallychallenged.Logging;
using musicallychallenged.Services.Sync.DTO;

namespace musicallychallenged.Services.Sync
{
    public sealed class PesnocloudIngestService : IPesnocloudIngestService, IDisposable
    {
        private readonly IBotConfiguration _configuration;
        private readonly PesnocloudConformer _conformer;
        private readonly HttpClient _httpClient;

        private static readonly ILog logger = Log.Get(typeof(PesnocloudIngestService));

        private const string BotTokenHeader = "X-Bot-Token";
        private const string BotSource = "challenged";

        /// <summary>
        ///     External IDs are strings that look like challenged-123, where first string is a source identifier
        ///     (challenged for modern bot, pollr for older pollr-voted entries etc) and number is
        ///     whatever internally assigned to round or entry
        /// </summary>
        private static string BuildExternalEntryId(int internalId) => $"{BotSource}-{internalId}";

        public PesnocloudIngestService(IBotConfiguration configuration, PesnocloudConformer conformer)
        {
            _configuration = configuration;
            _conformer = conformer;
            _httpClient = new HttpClient();
            
            logger.Info($"Looking for Pesnocloud service at {configuration.PesnocloudBaseUri}");
        }

        public async Task<bool> IsAlive(CancellationToken token)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get,
                    _configuration.PesnocloudBaseUri + $"/bot/isHealthy");
                request.Headers.Add(BotTokenHeader, _configuration.PesnocloudBotToken.Unprotect());

                using var requestTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestTimeoutSource.CancelAfter(TimeSpan.FromSeconds(_configuration.PesnocloudTimeoutSeconds));
                
                using var response = await _httpClient.SendAsync(request, requestTimeoutSource.Token);
                
                if(response.StatusCode == HttpStatusCode.Unauthorized)
                    logger.Error($"{response.StatusCode}: bot token invalid");

                return response.IsSuccessStatusCode;
            }
            catch (TaskCanceledException)
            {
                //Timeout - service is down
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task AddOrUpdateTrack(TrackAddedOrUpdatedSyncEvent syncEvent, CancellationToken cancellationToken)
        {
            string tempPayloadName = string.Empty;

            try
            {
                //Conform to standard pesnocloud mp3

                tempPayloadName = await _conformer.ConformAudio(
                    syncEvent.PayloadPath,
                    PathService.GetTempFilename() + ".mp3",
                    cancellationToken);

                await using var fileStream = File.OpenRead(tempPayloadName);

                var query = QueryHelpers.AddQueryString(
                    _configuration.PesnocloudBaseUri + "/bot/track",
                    new Dictionary<string, string>
                    {
                        ["authorId"] = syncEvent.AuthorId?.ToString() ?? string.Empty,
                        ["roundId"] = BuildExternalEntryId(syncEvent.InternalRoundNumber),
                        ["id"] = BuildExternalEntryId(syncEvent.InternalEntryId),
                        ["filename"] = syncEvent.PayloadTitle,
                        ["votes"] = syncEvent.Votes?.ToString() ?? "0",
                        ["submittedAt"] = syncEvent.SubmissionDate.ToString(CultureInfo.InvariantCulture),
                        ["authorName"] = syncEvent.Author?.Length > 128
                            ? syncEvent.Author[..128]
                            : syncEvent.Author,
                    });

                //Request body is a mp3 file

                var streamContent = new StreamContent(fileStream);

                //Avoid accidentally hitting netcore / http.sys / iis / whatever url length limits and
                //pass description as a header

                if (!string.IsNullOrEmpty(syncEvent.Description))
                    streamContent.Headers.Add("X-Entry-Description",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(syncEvent.Description)));

                AppendAuthHeader(streamContent.Headers);

                using var response = await _httpClient.PutAsync(
                    query,
                    streamContent,
                    cancellationToken);

                await EnsureSucceeded(response);
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPayloadName) && !string.Equals(tempPayloadName, syncEvent.PayloadPath))
                    File.Delete(tempPayloadName);
            }
        }

        private async Task EnsureSucceeded(HttpResponseMessage message)
        {
            if (!message.IsSuccessStatusCode)
                throw new Exception(
                    $"Event sync failed: {message.StatusCode}/{await message.Content.ReadAsStringAsync()}");
        }


        public async Task PatchTrack(TrackPatchSyncEvent patchSyncEvent, CancellationToken cancellationToken)
        {
            patchSyncEvent.Payload.Source = BotSource;

            var content = JsonContent.Create(patchSyncEvent.Payload);

            AppendAuthHeader(content.Headers);

            using var response = await _httpClient.PatchAsync(
                _configuration.PesnocloudBaseUri + "/bot/track",
                content,
                cancellationToken);
            
            await EnsureSucceeded(response);
        }

        public async Task PatchRound(RoundPatchSyncEvent patchSyncEvent, CancellationToken cancellationToken)
        {
            patchSyncEvent.Payload.Source = BotSource;

            var content = JsonContent.Create(patchSyncEvent.Payload);

            AppendAuthHeader(content.Headers);

            using var response = await _httpClient.PatchAsync(
                _configuration.PesnocloudBaseUri + "/bot/round",
                content,
                cancellationToken);
            
            await EnsureSucceeded(response);
        }

        public async Task DeleteTrack(TrackDeletedSyncEvent trackDeletedSync, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete,
                _configuration.PesnocloudBaseUri + $"/bot/track?id={trackDeletedSync.Id}");
            request.Headers.Add(BotTokenHeader, _configuration.PesnocloudBotToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            
            await EnsureSucceeded(response);
        }

        public Task Checkpoint(DebugCheckpointSyncEvent debugCheckpoint, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }


        public async Task StartOrUpdateRound(RoundStartedOrUpdatedSyncEvent roundStartedOrUpdated,
            CancellationToken cancellationToken)
        {
            var content = new StringContent(string.Empty);

            //Avoid accidentally hitting netcore / http.sys / iis / whatever url length limits and
            //pass description as a header

            if (!string.IsNullOrWhiteSpace(roundStartedOrUpdated.Description))
                content.Headers.Add("X-Entry-Description",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(roundStartedOrUpdated.Description)));

            AppendAuthHeader(content.Headers);

            var query = QueryHelpers.AddQueryString(
                _configuration.PesnocloudBaseUri + "/bot/round",
                new Dictionary<string, string>
                {
                    ["roundId"] = BuildExternalEntryId(roundStartedOrUpdated.InternalRoundNumber),
                    ["title"] = roundStartedOrUpdated.RoundTitle,
                    ["state"] = roundStartedOrUpdated.RoundState.ToString(),
                    ["start"] = roundStartedOrUpdated.StartDate?.ToString(),
                    ["end"] = roundStartedOrUpdated.EndDate?.ToString(),
                });

            using var response = await _httpClient.PutAsync(query, content, cancellationToken);
            
            await EnsureSucceeded(response);
        }


        public async Task UpdateVotes(VotesUpdatedSyncEvent syncEvent, CancellationToken cancellationToken)
        {
            var content = JsonContent.Create(new BotVotesSnapshot
            {
                Votes = syncEvent.VotesPerEntries.ToDictionary(pair => BuildExternalEntryId(pair.Key),
                    pair => pair.Value)
            });

            AppendAuthHeader(content.Headers);

            using var response = await _httpClient.PutAsync(_configuration.PesnocloudBaseUri + "/bot/votes", content,
                cancellationToken);
            
            await EnsureSucceeded(response);
        }

        private void AppendAuthHeader(HttpContentHeaders headers) =>
            headers.Add(BotTokenHeader, _configuration.PesnocloudBotToken.Unprotect());

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }


    public interface IPesnocloudIngestService
    {
        Task<bool> IsAlive(CancellationToken token);
        Task AddOrUpdateTrack(TrackAddedOrUpdatedSyncEvent syncEvent, CancellationToken cancellationToken);

        Task StartOrUpdateRound(RoundStartedOrUpdatedSyncEvent roundStartedOrUpdated,
            CancellationToken cancellationToken);

        Task UpdateVotes(VotesUpdatedSyncEvent voteUpdatedSyncEvent, CancellationToken cancellationToken);
        Task PatchTrack(TrackPatchSyncEvent patchSyncEvent, CancellationToken cancellationToken);
        Task PatchRound(RoundPatchSyncEvent patchSyncEvent, CancellationToken cancellationToken);
        Task DeleteTrack(TrackDeletedSyncEvent trackDeletedSync, CancellationToken cancellationToken);
        Task Checkpoint(DebugCheckpointSyncEvent debugCheckpoint, CancellationToken cancellationToken);
    }
}