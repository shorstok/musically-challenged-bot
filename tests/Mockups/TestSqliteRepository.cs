using System;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Data;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using NodaTime;
using NUnit.Framework;

namespace tests.Mockups
{
    public class TestSqliteRepository : RepositoryBase
    {
        private readonly TestContext _testContext;
        private static readonly ILog logger = Log.Get(typeof(TestSqliteRepository));

        private readonly string _connectionString;

        private static string GetTestDatabaseDir(TestContext testContext) => 
            Path.Combine(testContext.WorkDirectory, $"delete-me!");

        private static string GetTestDatabasePath(TestContext testContext) => 
            Path.Combine(GetTestDatabaseDir(testContext), $"bot-test-{testContext.Test.MethodName}.sqlite");

        protected override DbConnection CreateOpenConnection() => 
            new SQLiteConnection(_connectionString).OpenAndReturn();

        public TestSqliteRepository(TestContext testContext, IClock clock) : base(clock)
        {
            _testContext = testContext;
            
            var databasePath = GetTestDatabasePath(testContext);

            if (!Directory.Exists(GetTestDatabaseDir(testContext)))
                Directory.CreateDirectory(GetTestDatabaseDir(testContext));

            if (File.Exists(databasePath))
                File.Delete(databasePath);

            _connectionString = CreateConnectionString();
        }

        public string CreateConnectionString() => 
            $@"Data Source=""{GetTestDatabasePath(_testContext).Replace(@"\", @"\\")}""";
    }
}