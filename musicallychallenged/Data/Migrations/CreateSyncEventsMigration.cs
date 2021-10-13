using FluentMigrator;

namespace musicallychallenged.Data.Migrations
{
    [Migration(202110131752)]
    public class CreateSyncEventsMigration : AutoReversingMigration
    {
        public override void Up()
        {
            Create.Table("SyncEvent")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("CreatedAt").AsString(255).NotNullable()
                .WithColumn("SyncedAt").AsString(255).Nullable()
                .WithColumn("SerializedDto").AsString().NotNullable();
        }
    }
}