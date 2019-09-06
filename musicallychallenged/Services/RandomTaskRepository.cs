using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Data;
using musicallychallenged.Logging;
using NodaTime;

namespace musicallychallenged.Services
{
    public class RandomTaskRepository
    {
        private readonly IRepository _repository;
        private readonly IClock _clock;

        private static readonly ILog logger = Log.Get(typeof(RandomTaskRepository));

        private const string FallbackTask = "One chord song — один аккорд, но чтоб казалось движение";

        private static readonly Random Generator = new Random();

        public RandomTaskRepository(IRepository repository, IClock clock)
        {
            _repository = repository;
            _clock = clock;
        }

        public string GetRandomTaskDescription()
        {
            var availableTasks = _repository.GetLeastUsedRandomTasks();
            
            if (availableTasks?.Any()!=true)
            {
                logger.Error($"GetRandomTaskDescription: No available random tasks in repository!");
                return FallbackTask;
            }
            
            var selected = availableTasks.Length > 1 ? availableTasks[Generator.Next(availableTasks.Length - 1)] : availableTasks[0];

            selected.LastUsed = _clock.GetCurrentInstant();
            selected.UsedCount++;

            _repository.UpdateRandomTask(selected);

            return selected.Description;
        }
    }
}
