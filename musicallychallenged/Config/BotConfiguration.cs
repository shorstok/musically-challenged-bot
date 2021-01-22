using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using musicallychallenged.Localization;
using musicallychallenged.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NodaTime;

namespace musicallychallenged.Config
{
    [DataContract]
    public class BotDeployment
    {
        [JsonProperty("Name")]
        public string Name { get;set; }
        [JsonProperty("VotingChatId")]
        public long VotingChatId { get;set; }
        [JsonProperty("MainChatId")]
        public long MainChatId { get;set; }
    }

    [DataContract]
    public class PostponeOption
    {
        public enum DurationKind
        {
            Minutes,
            Days
        }

        public PostponeOption(double value, DurationKind kind)
        {
            Kind = kind;
            Value = value;
        }

        [JsonProperty("DurationKind")]
        [JsonConverter(typeof(StringEnumConverter))]
        public DurationKind Kind { get; set; }

        [JsonProperty("DurationValue")]
        public double Value { get; set; }

        [JsonIgnore]
        public Duration AsDuration
        {
            get
            {
                switch (Kind)
                {
                    case DurationKind.Minutes:
                        return Duration.FromMinutes(Value);
                    case DurationKind.Days:
                        return Duration.FromDays(Value);
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Kind), Kind, $"Dont know what {Kind} is");
                }
            }
        }

        public string GetLocalizedName(LocStrings loc)
        {
            switch (Kind)
            {
                case DurationKind.Minutes:
                    return $"{Value} {loc.DimMinutes}";
                case DurationKind.Days:
                    return $"{Value} {loc.DimDays}";
                default:
                    return $"{Value} {Kind}";
            }
        }
    }


    [DataContract]
    public class BotConfiguration : IBotConfiguration
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

        [JsonProperty("PostponeHoursAllowed")]
        public double PostponeHoursAllowed { get; set; } = 7*2*24 + 1; //two weeks plus 1 hour for last-time entries

        [JsonProperty("PostponeOptions")]
        public PostponeOption[] PostponeOptions { get; set; } = {
            new PostponeOption(15, PostponeOption.DurationKind.Minutes), 
            new PostponeOption(1, PostponeOption.DurationKind.Days), 
            new PostponeOption(3, PostponeOption.DurationKind.Days), 
            new PostponeOption(7, PostponeOption.DurationKind.Days), 
        };

        [JsonProperty("Deployment")]
        public BotDeployment[] Deployments { get; set; } = new BotDeployment[]
        {
            new BotDeployment{Name = "Production",MainChatId = 1, VotingChatId = 1},
            new BotDeployment{Name = "Staging",MainChatId = 1, VotingChatId = 1},
            new BotDeployment{Name = "Alpha",MainChatId = 1, VotingChatId = 1},
        };

        [JsonProperty("DeadlinePollingPeriodMs")]
        public int DeadlinePollingPeriodMs { get; set; } = 15000;

        //How many users required to trigger postpone
        [JsonProperty("PostponeQuorum")]
        public int PostponeQuorum { get; set; } = 3;

        [JsonProperty("TaskSuggestionCollectionDeadlineTimeHours")]
        public int TaskSuggestionCollectionDeadlineTimeHours { get; set; } = 12;

        [JsonProperty("TaskSuggestionVotingDeadlineTimeHours")]
        public int TaskSuggestionVotingDeadlineTimeHours { get; set; } = 12;

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