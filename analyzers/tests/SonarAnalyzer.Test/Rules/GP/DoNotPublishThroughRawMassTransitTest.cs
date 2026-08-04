using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotPublishThroughRawMassTransitTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotPublishThroughRawMassTransit>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
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
        }

        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                Task Publish<T>(T @event, CancellationToken cancellationToken = default) where T : class;
            }
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
                    _publishEndpoint.Publish(@event); // Noncompliant {{Publish through Juno (IPublisher / IMessageSender) instead of MassTransit's 'Publish'.}}
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
                    _bus.Publish(@event); // Noncompliant {{Publish through Juno (IPublisher / IMessageSender) instead of MassTransit's 'Publish'.}}
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
                    _provider.GetSendEndpoint(new System.Uri("queue:orders")); // Noncompliant {{Publish through Juno (IPublisher / IMessageSender) instead of MassTransit's 'GetSendEndpoint'.}}
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
}
