using System.Data.Common;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.IO;
using musicallychallenged.Config;
using musicallychallenged.Services;
using NodaTime;

namespace musicallychallenged.Data
{
    public class SqliteRepository : RepositoryBase
    {
        private readonly string _connectionString;

        protected override DbConnection CreateOpenConnection() => new SQLiteConnection(_connectionString).OpenAndReturn();

        public SqliteRepository(IClock clock) : base(clock) => _connectionString = CreateConnectionString();

        public static string CreateConnectionString() => $@"Data Source=""{PathService.BotDbPath.Replace(@"\", @"\\")}""";
    }
}