using System;
using Dapper.Contrib.Extensions;
using NodaTime;

namespace musicallychallenged.Domain
{
    [Table("ActiveChat")]
    public class ActiveChat
    {        
        [ExplicitKey]
        public long Id { get; set; }

        public string Name{ get; set; }
        public Instant Timestamp { get; set; }      
    }
}