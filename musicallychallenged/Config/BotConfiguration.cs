using System;
using System.IO;
using System.Runtime.Serialization;
using musicallychallenged.Services;
using Newtonsoft.Json;

namespace musicallychallenged.Config
{
    [DataContract]
    public class BotConfiguration
    {
        private static readonly string ConfigFilename;

        private static readonly object _configReaderWriterLock = new object();

        static BotConfiguration()
        {
            ConfigFilename = Path.Combine(PathService.AppData, "service-config.json");
        }


        //Set cleartext data with cleartext: prefix, they would be replaced with protected text on load
        [ProtectedString]
        [JsonProperty("telegram_botkey")]
        public string TelegramAnnouncerBotKey { get; set; } = "cleartext:dummy_botkey";

        [JsonProperty("TelegramBotId")]
        public int TelegramBotId { get; set; } = 741897987;

        [JsonProperty("telegram_max_messages_per_sec")]
        public int TelegramMaxMessagesPerSecond { get; set; } = 15;

        [JsonProperty("dialog_inactivity_timeout_minutes")]
        public double DialogInactivityTimeoutMinutes { get; set; } = 10 * 60;

        [JsonProperty("MinAllowedVoteCountForWinners")]
        public int MinAllowedVoteCountForWinners { get; set; } = 2;

        [JsonProperty("MinAllowedContestEntriesToStartVoting")]
        public int MinAllowedContestEntriesToStartVoting { get; set; } = 2;

        [JsonProperty("MaxTaskSelectionTimeHours")]
        public double MaxTaskSelectionTimeHours { get; set; } = 4;

        [JsonProperty("MaxAdminVotingTimeHoursSinceFirstVote")]
        public double MaxAdminVotingTimeHoursSinceFirstVote { get; set; } = 1;

        [JsonProperty("AnnouncementTimeZone")]
        public string AnnouncementTimeZone { get; set; } = "Europe/Moscow";

        [JsonProperty("AnnouncementTimeDescriptor")]
        public string AnnouncementTimeDescriptor { get; set; } = "МСК";

        [JsonProperty("AnnouncementDateTimeFormat")]
        public string AnnouncementDateTimeFormat { get; set; } = "dd.MM HH:mm z";

        [JsonProperty("VotingChannelInviteLink")]
        public string VotingChannelInviteLink { get; set; } = "https://t.me/joinchat/AAAAAFJ0z1wzePGwcp5uKQ";

        [JsonProperty("MinVoteValue")]
        public int MinVoteValue { get; set; } = 1;
        
        [JsonProperty("MaxVoteValue")]
        public int MaxVoteValue { get; set; } = 5;

        [JsonProperty("SubmissionTimeoutMinutes")]
        public int SubmissionTimeoutMinutes { get; set; } = 20;

        [JsonProperty("ContestDeadlineEventPreviewTimeHours")]
        public double ContestDeadlineEventPreviewTimeHours { get; set; } = 8;

        [JsonProperty("VotingDeadlineEventPreviewTimeHours")]
        public double VotingDeadlineEventPreviewTimeHours { get; set; } = 12;

        public static BotConfiguration LoadOrCreate(bool saveIfNew = false)
        {
            lock (_configReaderWriterLock)
            {
                if (!File.Exists(ConfigFilename))
                {
                    var result = new BotConfiguration();

                    if (saveIfNew)
                        result.Save();

                    return result;
                }

                var existing = JsonConvert.DeserializeObject<BotConfiguration>(File.ReadAllText(ConfigFilename),
                    JsonFormatters.IndentedAutotype);

                if (CurrentUserProtectedString.GenerateProtectedPropertiesFromCleartext(existing))
                    existing.Save();

                return existing;
            }
        }

        public bool Reload()
        {
            lock(_configReaderWriterLock)
            {
                if (!File.Exists(ConfigFilename))
                    return false;
            
                JsonConvert.PopulateObject(File.ReadAllText(ConfigFilename),
                    this,
                    JsonFormatters.IndentedAutotype);

                return true;
            }           
        }

        public void Save()
        {
            lock(_configReaderWriterLock)
                File.WriteAllText(ConfigFilename, JsonConvert.SerializeObject(this, JsonFormatters.IndentedAutotype));
        }
    }
}