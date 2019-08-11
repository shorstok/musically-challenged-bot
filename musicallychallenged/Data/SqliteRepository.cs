using System.Data.Common;
using System.Data.SqlClient;
using System.Data.SQLite;
using musicallychallenged.Services;
using NodaTime;

namespace musicallychallenged.Data
{
    public class SqliteRepository : RepositoryBase
    {
        private readonly string _connectionString;

        protected override DbConnection CreateOpenConnection()
        {
            return new SQLiteConnection(_connectionString).OpenAndReturn();
        }

        public SqliteRepository(IClock clock) : base(clock)
        {
            _connectionString = $@"Data Source=""{(PathService.AppData + @"\bot.sqlite").Replace(@"\", @"\\")}""";
        }
    }
}