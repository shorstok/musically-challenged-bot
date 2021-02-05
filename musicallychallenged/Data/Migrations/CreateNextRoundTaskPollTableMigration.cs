using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentMigrator;

namespace musicallychallenged.Data.Migrations
{
    [Migration(202101200207)]
    public class CreateNextRoundTaskPollTableMigration : AutoReversingMigration
    {
        public override void Up()
        {
            Create.Table("NextRoundTaskPoll")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("Timestamp").AsString(255).NotNullable()
                .WithColumn("State").AsInt32().NotNullable()
                .WithColumn("WinnerId").AsInt32().Nullable();
        }
    }
}
