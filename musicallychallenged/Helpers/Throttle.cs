using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace musicallychallenged.Helpers
{
    /// <summary>
    /// Executes given task less often than TimeSpan provided
    /// Ensures action 
    /// </summary>
    public class Throttle
    {
        private readonly TimeSpan _timeSpan;
        private DateTime? _lastExecutionDateTime = null;
        private int _identifier = 0;

        public Throttle(TimeSpan timeSpan)
        {
            _timeSpan = timeSpan;
        }

        public async Task<bool> WaitAsync(Func<Task> actionToThrottle, CancellationToken cancellationToken)
        {
            try
            {
                var self = Interlocked.Increment(ref _identifier);
                   
                if (self > 2)
                    return false;

                if (self == 2)
                {
                    if(_lastExecutionDateTime == null)
                        await Task.Delay(_timeSpan, cancellationToken);
                    else
                    {
                        var delta = _timeSpan - (DateTime.UtcNow - _lastExecutionDateTime.Value);

                        if (delta.TotalSeconds > 0)
                            await Task.Delay(delta, cancellationToken);
                    }
                }

                _lastExecutionDateTime = DateTime.UtcNow;
                    
                if(_identifier == 1)
                    await actionToThrottle();

                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _identifier);
            }
        }

    }
}
