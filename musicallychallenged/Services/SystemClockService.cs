using NodaTime;

namespace musicallychallenged.Services
{
    public class SystemClockService : IClock
    {
        public Instant GetCurrentInstant()
        {
            return SystemClock.Instance.GetCurrentInstant();
        }
    }
}
