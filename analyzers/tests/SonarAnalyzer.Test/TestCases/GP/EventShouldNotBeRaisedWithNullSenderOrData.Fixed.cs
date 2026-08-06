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
    }
}
