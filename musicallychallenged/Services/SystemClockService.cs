using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
