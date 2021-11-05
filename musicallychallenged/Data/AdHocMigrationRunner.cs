using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FluentMigrator.Runner;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using musicallychallenged.Logging;
using musicallychallenged.Services;

namespace musicallychallenged.Data
{
    public class AdHocMigrationRunner
    {
        private readonly string _connectionString;
        private readonly bool _createRepositoryIfNotExists;

        private static readonly ILog logger = Log.Get(typeof(AdHocMigrationRunner));

        public AdHocMigrationRunner(string connectionString, bool createRepositoryIfNotExists)
        {
            _connectionString = connectionString;
            _createRepositoryIfNotExists = createRepositoryIfNotExists;
        }

        public void RunMigrations()
        {
            var serviceProvider = CreateServices();

            // Put the database update into a scope to ensure
            // that all resources will be disposed.
            using (var scope = serviceProvider.CreateScope())
            {
                if (_createRepositoryIfNotExists && !File.Exists(PathService.BotDbPath))
                    CreateInitialSchema();

                UpdateDatabase(scope.ServiceProvider);
            }
        }
        
        
        private void CreateInitialSchema()
        {
            const string schemaResourceKey = @"bot.sqlite.sql";
            
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
                
                using (var connection = new SQLiteConnection(_connectionString).OpenAndReturn())
                    connection.Execute(schema);
            }
        }

        /// <summary>
        /// Configure the dependency injection services
        /// </summary>
        private IServiceProvider CreateServices()
        {
            return new ServiceCollection()
                // Add common FluentMigrator services
                .AddFluentMigratorCore().ConfigureRunner(rb => rb
                    // Add SQLite support to FluentMigrator
                    .AddSQLite()
                    // Set the connection string
                    .WithGlobalConnectionString(_connectionString)
                    // Define the assembly containing the migrations
                    .ScanIn(typeof(IRepository).Assembly).For.Migrations())
                // Enable logging to console in the FluentMigrator way
                .AddLogging(lb => lb.AddFluentMigratorConsole())
                // Build the service provider
                .BuildServiceProvider(false);
        }

        /// <summary>
        /// Update the database
        /// </summary>
        private static void UpdateDatabase(IServiceProvider serviceProvider)
        {
            // Instantiate the runner
            var runner = serviceProvider.GetRequiredService<IMigrationRunner>();

            // Execute the migrations
            runner.MigrateUp();
        }
    }
}
