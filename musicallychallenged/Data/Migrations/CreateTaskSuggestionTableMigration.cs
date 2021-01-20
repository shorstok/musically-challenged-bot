using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentMigrator;

namespace musicallychallenged.Data.Migrations
{
    [Migration(202101200218)]
    class CreateTaskSuggestionTableMigration : AutoReversingMigration
    {
        public override void Up()
        {
            Create.Table("TaskSuggestion")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("AuthorId").AsInt32().NotNullable()
                .WithColumn("PollId").AsInt32().NotNullable()
                .WithColumn("Timestamp").AsString(255).NotNullable()
                .WithColumn("Description").AsString(4096).NotNullable()
                .WithColumn("ConsolidatedVoteCount").AsInt32().Nullable()
                .WithColumn("ContainerChatId").AsInt64().NotNullable()
                .WithColumn("ContainerMesssageId").AsInt32().NotNullable();
        }
    }
}
