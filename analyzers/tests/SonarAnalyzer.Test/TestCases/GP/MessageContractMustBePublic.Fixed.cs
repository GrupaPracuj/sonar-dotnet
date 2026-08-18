namespace MassTransit
{
    public interface IConsumer<T> where T : class
    {
        System.Threading.Tasks.Task Consume(T message);
    }

    public sealed class StateMachinePublisher
    {
        public System.Threading.Tasks.Task Publish<TSaga, TData, TMessage>(TMessage message) where TMessage : class =>
            System.Threading.Tasks.Task.CompletedTask;
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
    public sealed class OrderSaga { }
    public sealed class OrderData { }
    public sealed class OrderShipped { }

    public class OrderService
    {
        private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

        public System.Threading.Tasks.Task Accept(System.Guid id) =>
            _publisher.Publish(new OrderAccepted(id)); // Fixed

        public System.Threading.Tasks.Task Ship(MassTransit.StateMachinePublisher publisher) =>
            publisher.Publish<OrderSaga, OrderData, OrderShipped>(new OrderShipped()); // Fixed
    }
}
