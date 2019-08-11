using System;

namespace musicallychallenged.Services.Events
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    public class AggregateSubscription<TMessage> : ISubscription<TMessage> where TMessage : IAggregateMessage
    {
        public Action<TMessage> Action { get; }
        public IEventAggregator EventAggregator { get; }

        public AggregateSubscription(IEventAggregator eventAggregator, Action<TMessage> action)
        {
            if (eventAggregator == null) throw new ArgumentNullException(nameof(eventAggregator));
            if (action == null) throw new ArgumentNullException(nameof(action));

            EventAggregator = eventAggregator;
            Action = action;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                EventAggregator.Unsubscribe(this);
        }
    }
}
