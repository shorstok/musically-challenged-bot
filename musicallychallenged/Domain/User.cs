using System;
using Dapper.Contrib.Extensions;
using musicallychallenged.Administration;
using NodaTime;

namespace musicallychallenged.Domain
{
    public enum UserState
    {
        Default = 0,
        Banned =1
    }

    [Table("User")]
    public class User
    {
        //Matches Telegram user Id
        [ExplicitKey]
        public long Id { get; set; }

        public string Username { get; set; }
        public string Name { get; set; }

        public long? ChatId { get; set; }
        
        public Instant LastActivityUTC { get; set; }

        public bool ReceivesNotificatons { get; set; }

        public UserState State { get; set; }
        public UserCredentials Credentials { get; set; }

        public long Pesnocent { get; set; }

        public string GetUsernameOrNameWithCircumflex() => string.IsNullOrEmpty(Username) ? Name : $"@{Username}";

        public string GetHtmlUserLink() => $"<a href=\"tg://user?id={Id}\">🎧{Username ?? Name}</a>";
    }
}