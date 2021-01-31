using FluentMigrator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace musicallychallenged.Data.Migrations
{
    [Migration(202101282148)]
    public class AlterSystemStateCreateColumnCurrentTaskKindMigration : AutoReversingMigration
    {
        public override void Up()
        {
            Alter.Table("SystemState")
                .AddColumn("CurrentTaskKind")
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(0);
        }
    }
}
