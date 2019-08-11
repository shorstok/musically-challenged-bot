using System;

namespace musicallychallenged.Services.Events
{
    /// <summary>
    /// 
    /// </summary>
    public interface IEventAggregator
    {
        void Publish<TMessage>(TMessage message) where TMessage : IAggregateMessage;

        ISubscription<TMessage> Subscribe<TMessage>(Action<TMessage> action) where TMessage : IAggregateMessage;

        void Unsubscribe<TMessage>(ISubscription<TMessage> subscription) where TMessage : IAggregateMessage;

        void ClearAllSubscriptions();
        void ClearAllSubscriptions(Type[] exceptMessages);
    }
}