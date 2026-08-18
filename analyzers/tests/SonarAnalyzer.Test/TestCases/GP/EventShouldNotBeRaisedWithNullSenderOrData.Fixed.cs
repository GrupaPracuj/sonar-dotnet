using System;

namespace Tests.Diagnostics
{
    public class EventShouldNotBeRaisedWithNullSenderOrData
    {
        public event EventHandler Shipped;

        public void RaiseWithNullSender()
        {
            Shipped(this, EventArgs.Empty); // Fixed
        }

        public void RaiseWithNullSenderConditional()
        {
            Shipped?.Invoke(this, EventArgs.Empty); // Fixed
        }

        public static void RaiseOnAnotherInstance(EventShouldNotBeRaisedWithNullSenderOrData source)
        {
            source.Shipped?.Invoke(source, EventArgs.Empty); // Fixed
        }

        public static void RaiseOnComputedInstance()
        {
            Source().Shipped?.Invoke(null, EventArgs.Empty); // Fixed
        }

        private static EventShouldNotBeRaisedWithNullSenderOrData Source() => new();
    }
}
