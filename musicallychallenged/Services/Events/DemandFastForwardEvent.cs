namespace musicallychallenged.Services.Events
{
    public class DemandFastForwardEvent : IAggregateMessage
    {
        public bool IsPreDeadline { get; }

        public DemandFastForwardEvent(bool isPreDeadline)
        {
            IsPreDeadline = isPreDeadline;
        }
    }
}