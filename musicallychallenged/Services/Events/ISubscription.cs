using System;

namespace musicallychallenged.Services.Events
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    public interface ISubscription<in TMessage> : ISubscription where TMessage : IAggregateMessage
    {
        Action<TMessage> Action { get; }
    }

    public interface ISubscription : IDisposable
    {
        IEventAggregator EventAggregator { get; }
    }
}