using FluentMigrator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace musicallychallenged.Data.Migrations
{
    [Migration(202101200250)]
    class CreateTaskPollVoteTableMigration : AutoReversingMigration
    {
        public override void Up()
        {
            Create.Table("TaskPollVote")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("UserId").AsInt32().NotNullable()
                .WithColumn("TaskSuggestionId").AsInt32().NotNullable()
                .WithColumn("Timestamp").AsString(255).NotNullable()
                .WithColumn("Value").AsInt32().NotNullable();
        }
    }
}
