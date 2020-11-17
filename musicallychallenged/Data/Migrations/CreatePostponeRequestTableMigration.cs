using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentMigrator;

namespace musicallychallenged.Data.Migrations
{
    [Migration(202011170919)]
    public class CreatePostponeRequestTableMigration : AutoReversingMigration
    {
        public override void Up()
        {
            Create.Table("PostponeRequest").
                WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity().
                WithColumn("UserId").AsInt64().NotNullable().
                WithColumn("ChallengeRoundNumber").AsInt64().NotNullable().
                WithColumn("AmountMinutes").AsInt64().NotNullable().
                WithColumn("State").AsInt32().NotNullable().
                WithColumn("Timestamp").AsString(255).Nullable();
        }
    }
}
