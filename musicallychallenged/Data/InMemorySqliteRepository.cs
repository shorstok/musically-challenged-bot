using System;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using log4net;
using musicallychallenged.Logging;
using NodaTime;

namespace musicallychallenged.Data
{
    public sealed class InMemorySqliteRepository : RepositoryBase, IDisposable
    {
        private static readonly ILog logger = Log.Get(typeof(InMemorySqliteRepository));

        private readonly string _connectionString;

        private const string schemaResourceKey = @"bot.sqlite.sql";

        //SQLite :memory: database with shared cache exists only till last connection to this database closes
        private SQLiteConnection _keepaliveConnection = null;

        protected override DbConnection CreateOpenConnection()
        {
            return new SQLiteConnection(_connectionString).OpenAndReturn();
        }

        /// <summary>
        /// In-memory db has to recreate schema each time
        /// </summary>
        private void CreateSchema()
        {
            var cfgResourceName = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .FirstOrDefault(e => e.IndexOf(schemaResourceKey, StringComparison.Ordinal) != -1);

            string schema = null;

            if (cfgResourceName == null)
            {
                logger.Error($"Key `{schemaResourceKey}` not found in embedded resources");
                return;
            }

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(cfgResourceName))
            {
                if (null == stream)
                {
                    logger.Error($"Error opening resource `{cfgResourceName}`");
                    return;
                }

                using (var sr = new StreamReader(stream))
                    schema = sr.ReadToEnd();
            }
           
            _keepaliveConnection.Execute(schema);
        }

        public InMemorySqliteRepository(IClock clock) : base(clock)
        {
            _connectionString = $"FullUri=file::memory:?cache=shared;ToFullPath=false";

            _keepaliveConnection = CreateOpenConnection() as SQLiteConnection;

            CreateSchema();
        }

        public void Dispose()
        {
            _keepaliveConnection?.Close();
            _keepaliveConnection = null;
        }
    }
}