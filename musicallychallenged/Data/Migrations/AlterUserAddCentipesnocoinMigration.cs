using FluentMigrator;

namespace musicallychallenged.Data.Migrations
{
    [Migration(202601040001)]
    public class AlterUserAddCentipesnocoinMigration : AutoReversingMigration
    {
        public override void Up()
        {
            Alter.Table("User")
                .AddColumn("Pesnocent")
                .AsInt64()
                .NotNullable()
                .WithDefaultValue(500); // 5 pesnocoin bonus for existing users
        }
    }
}
