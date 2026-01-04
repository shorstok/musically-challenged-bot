using FluentMigrator;

namespace musicallychallenged.Data.Migrations
{
    [Migration(202601040002)]
    public class AlterPostponeRequestAddCostMigration : AutoReversingMigration
    {
        public override void Up()
        {
            Alter.Table("PostponeRequest")
                .AddColumn("CostPesnocents")
                .AsInt64()
                .NotNullable()
                .WithDefaultValue(0);
        }
    }
}
