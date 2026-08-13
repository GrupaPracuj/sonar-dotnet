using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotPublishThroughRawMassTransitTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotPublishThroughRawMassTransit>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        using MassTransit;
        using System.Threading;
        using System.Threading.Tasks;

        namespace MassTransit
        {
            public interface IPublishEndpoint
            {
                Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class;
            }

            public interface ISendEndpoint
            {
                Task Send<T>(T message) where T : class;
            }

            public interface ISendEndpointProvider
            {
                Task<ISendEndpoint> GetSendEndpoint(System.Uri address);
            }

            public interface IBus : IPublishEndpoint, ISendEndpointProvider { }

            public interface IConsumer<T> where T : class
            {
                Task Consume(T message);
            }

            public static class SendEndpointProviderExtensions
            {
                public static Task Send<T>(this ISendEndpointProvider provider, T message) where T : class =>
                    Task.CompletedTask;
            }
        }

        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                Task Publish<T>(T @event, CancellationToken cancellationToken = default) where T : class;
            }
        }

        namespace GP.Juno.EventStream
        {
            public interface EventStream : MassTransit.IPublishEndpoint, MassTransit.ISendEndpointProvider { }
        }

        public class OrderAccepted { }
        """;

    [TestMethod]
    public void DoNotPublishThroughRawMassTransit_NoncompliantForPublishEndpoint() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly MassTransit.IPublishEndpoint _publishEndpoint;

                public Task Save(OrderAccepted @event) =>
                    _publishEndpoint.Publish(@event); // Noncompliant {{Publish through Juno (IPublisher / IMessageSender / EventStream) instead of MassTransit's 'Publish'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotPublishThroughRawMassTransit_NoncompliantForBus() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly MassTransit.IBus _bus;

                public Task Save(OrderAccepted @event) =>
                    _bus.Publish(@event); // Noncompliant {{Publish through Juno (IPublisher / IMessageSender / EventStream) instead of MassTransit's 'Publish'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotPublishThroughRawMassTransit_NoncompliantForGetSendEndpoint() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly MassTransit.ISendEndpointProvider _provider;

                public Task<MassTransit.ISendEndpoint> Endpoint() =>
                    _provider.GetSendEndpoint(new System.Uri("queue:orders")); // Noncompliant {{Publish through Juno (IPublisher / IMessageSender / EventStream) instead of MassTransit's 'GetSendEndpoint'.}}
            }
            """)
            .Verify();

    // Request/response is a third way out through MassTransit and needs the same treatment as publish and send.
    [TestMethod]
    public void DoNotPublishThroughRawMassTransit_NoncompliantForRequestClient() =>
        builder.AddSnippet(
            Stubs + """

            namespace MassTransit
            {
                public interface Response<T> where T : class { }

                public interface IRequestClient<TRequest> where TRequest : class
                {
                    Task<Response<TResponse>> GetResponse<TResponse>(TRequest request) where TResponse : class;
                }
            }

            public class OrderStatus { }

            public class OrderService
            {
                private readonly MassTransit.IRequestClient<OrderAccepted> _client;

                public Task<MassTransit.Response<OrderStatus>> Ask(OrderAccepted request) =>
                    _client.GetResponse<OrderStatus>(request); // Noncompliant {{Publish through Juno (IPublisher / IMessageSender / EventStream) instead of MassTransit's 'GetResponse'.}}
            }
            """)
            .Verify();

    // Consuming has no Juno wrapper - Juno's own examples implement IConsumer<T>.
    [TestMethod]
    public void DoNotPublishThroughRawMassTransit_CompliantForConsumer() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderAcceptedConsumer : MassTransit.IConsumer<OrderAccepted>
            {
                public Task Consume(OrderAccepted message) => Task.CompletedTask;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotPublishThroughRawMassTransit_CompliantForJunoPublisher() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public Task Save(OrderAccepted @event, CancellationToken cancellationToken) =>
                    _publisher.Publish(@event, cancellationToken);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotPublishThroughRawMassTransit_CompliantForJunoEventStream() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.EventStream.EventStream _eventStream;

                public Task Publish(OrderAccepted @event) =>
                    _eventStream.Publish(@event);

                public Task Send(OrderAccepted command) =>
                    _eventStream.Send(command);

                public Task<MassTransit.ISendEndpoint> Endpoint() =>
                    _eventStream.GetSendEndpoint(new System.Uri("queue:orders"));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotPublishThroughRawMassTransit_NoncompliantForUnrelatedWrapper() =>
        builder.AddSnippet(
            Stubs + """

            public interface OwnEventStream : MassTransit.IPublishEndpoint { }

            public class OrderService
            {
                private readonly OwnEventStream _eventStream;

                public Task Publish(OrderAccepted @event) =>
                    _eventStream.Publish(@event); // Noncompliant
            }
            """)
            .Verify();
}
