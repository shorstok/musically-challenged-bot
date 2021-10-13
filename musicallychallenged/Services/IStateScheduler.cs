using System;
using System.Threading.Tasks;

namespace musicallychallenged.Services
{
    public interface IStateScheduler
    {
        Task Activate();
        void Stop();

        event Action PreviewDeadlineHit;
        event Action DeadlineHit;
    }
}
