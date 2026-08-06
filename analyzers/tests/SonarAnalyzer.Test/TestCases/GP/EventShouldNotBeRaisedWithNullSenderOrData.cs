using System;

namespace Tests.Diagnostics
{
    public class EventShouldNotBeRaisedWithNullSenderOrData
    {
        public event EventHandler Shipped;

        public void RaiseWithNullSender()
        {
            Shipped(null, EventArgs.Empty); // Noncompliant {{Do not pass null as the sender - use 'this' (or the actual raising instance) so subscribers know who raised 'Shipped'.}}
        }

        public void RaiseWithNullSenderConditional()
        {
            Shipped?.Invoke(null, EventArgs.Empty); // Noncompliant {{Do not pass null as the sender - use 'this' (or the actual raising instance) so subscribers know who raised 'Shipped'.}}
        }
    }
}
