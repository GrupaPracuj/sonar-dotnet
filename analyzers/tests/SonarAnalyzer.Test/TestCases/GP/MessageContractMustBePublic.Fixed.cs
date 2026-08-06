namespace MassTransit
{
    public interface IConsumer<T> where T : class
    {
        System.Threading.Tasks.Task Consume(T message);
    }
}

namespace GP.Juno.Abstractions.EventStream
{
    public interface IPublisher
    {
        System.Threading.Tasks.Task Publish<T>(T @event) where T : class;
    }
}

namespace Tests.Diagnostics
{
    public sealed record OrderAccepted(System.Guid OrderId);

    public class OrderService
    {
        private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

        public System.Threading.Tasks.Task Accept(System.Guid id) =>
            _publisher.Publish(new OrderAccepted(id)); // Fixed
    }
}
