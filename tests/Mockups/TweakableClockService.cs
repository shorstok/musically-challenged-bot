using NodaTime;

namespace tests.Mockups
{
    public class TweakableClockService : IClock
    {
        public Duration Offset { get; set; } = Duration.Zero;
        
        public Instant GetCurrentInstant()
        {
            return SystemClock.Instance.GetCurrentInstant().Plus(Offset);
        }
    }
}