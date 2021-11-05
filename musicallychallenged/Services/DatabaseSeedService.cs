using System;
using log4net;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Logging;

namespace musicallychallenged.Services
{
    public class DatabaseSeedService
    {
        private readonly IRepository _repository;

        private static readonly ILog logger = Log.Get(typeof(DatabaseSeedService));

        public DatabaseSeedService(IRepository repository)
        {
            _repository = repository;
        }

        

        public void Run()
        {
            var seedAdministrators = Environment.GetEnvironmentVariable("Seed_AdministratorIds")?.Split(';') ??
                                     Array.Empty<string>();

            if (seedAdministrators.Length > 0)
            {
                logger.Info($"Seeding {seedAdministrators.Length} administrator users");

                foreach (var seedAdministrator in seedAdministrators)
                {
                    if (!long.TryParse(seedAdministrator, out var id))
                    {
                        logger.Warn($"`{seedAdministrator}` is not a valid userid");
                        continue;
                    }

                    _repository.MarkUserAsAdministrator(id);
                }
            }
            
            var votingChannelIdOverride = Environment.GetEnvironmentVariable("Seed__VotingChannelId");
            var mainChannelIdOverride = Environment.GetEnvironmentVariable("Seed__MainChannelId");

            if (votingChannelIdOverride != null && long.TryParse(votingChannelIdOverride, out var ovr))
            {
                logger.Info($"Seeding VotingChannelId");
                _repository.UpdateState(state => state.VotingChannelId, ovr);
            }
            if (votingChannelIdOverride != null && long.TryParse(mainChannelIdOverride, out var ovrMain))
            {
                logger.Info($"Seeding MainChannelId");
                _repository.UpdateState(state => state.MainChannelId, ovrMain);
            }
            
            
            

        }
    }
}