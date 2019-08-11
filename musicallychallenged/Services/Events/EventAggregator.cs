using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace musicallychallenged.Services.Events
{
    /// <summary>
    /// 
    /// </summary>
    public class EventAggregator : IEventAggregator
    {
        private readonly ConcurrentDictionary<Type, IList> _subscriptions = new ConcurrentDictionary<Type, IList>();
        private readonly object _locker = new object();

        public void Publish<TMessage>(TMessage message) where TMessage : IAggregateMessage
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            List<ISubscription<TMessage>> subscriptionList;

            lock (_locker)
            {
                if (!_subscriptions.TryGetValue(typeof(TMessage), out var sublist))
                    return;

                subscriptionList = new List<ISubscription<TMessage>>(sublist.Cast<ISubscription<TMessage>>());                
            }

            foreach (var subscription in subscriptionList)
                subscription?.Action(message);
        }

        public ISubscription<TMessage> Subscribe<TMessage>(Action<TMessage> action)
            where TMessage : IAggregateMessage
        {
            var messageType = typeof(TMessage);
            var subscription = new AggregateSubscription<TMessage>(this, action);

            lock (_locker)
            {
                if (_subscriptions.TryGetValue(messageType, out var list))
                    list.Add(subscription);
                else
                    _subscriptions.TryAdd(messageType, new List<ISubscription<TMessage>> { subscription });

                return subscription;
            }
        }

        public void Unsubscribe<TMessage>(ISubscription<TMessage> subscription)
            where TMessage : IAggregateMessage
        {
            lock (_locker)
            {
                if (_subscriptions.TryGetValue(typeof(TMessage), out var sublist))
                    sublist.Remove(subscription);
            }
        }

        public void ClearAllSubscriptions()
        {
            ClearAllSubscriptions(null);
        }

        public void ClearAllSubscriptions(Type[] exceptMessages)
        {
            lock (_locker)
            {
                var subs = new Dictionary<Type, IList>(_subscriptions);

                foreach (var messageSubscriptions in subs)
                {
                    bool canDelete = true;
                    if (exceptMessages != null)
                        canDelete = !exceptMessages.Contains(messageSubscriptions.Key);

                    if (canDelete)
                    {
                        IList dummy;
                        _subscriptions.TryRemove(messageSubscriptions.Key, out dummy);
                    }
                }
            }
        }
    }
}
