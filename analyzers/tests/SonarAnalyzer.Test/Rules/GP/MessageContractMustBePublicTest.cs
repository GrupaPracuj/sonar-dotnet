using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class MessageContractMustBePublicTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.MessageContractMustBePublic>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
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
        """;

    [TestMethod]
    public void MessageContractMustBePublic_NoncompliantForInternalContract() =>
        builder.AddSnippet(
            Stubs + """

            internal sealed record OrderAccepted(System.Guid OrderId);

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new OrderAccepted(id)); // Noncompliant {{'OrderAccepted' is not public, so no other service can reference this contract.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageContractMustBePublic_NoncompliantForInternalConsumedContract() =>
        builder.AddSnippet(
            Stubs + """

            internal sealed record OrderAccepted(System.Guid OrderId);

            // The consumer has to be internal too - C# will not let a public method take a less accessible parameter,
            // which is itself a hint that the contract is in the wrong place.
            internal class OrderConsumer : MassTransit.IConsumer<OrderAccepted> // Noncompliant {{'OrderAccepted' is not public, so no other service can reference this contract.}}
            {
                public System.Threading.Tasks.Task Consume(OrderAccepted message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageContractMustBePublic_CompliantForPublicContract() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record OrderAccepted(System.Guid OrderId);

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new OrderAccepted(id));
            }
            """)
            .VerifyNoIssues();

    // A same-named IConsumer<T> outside MassTransit is not messaging, so an internal contract consumed through it
    // is not this rule's business.
    [TestMethod]
    public void MessageContractMustBePublic_CompliantForUnrelatedOwnConsumerOfInternalContract() =>
        builder.AddSnippet(
            Stubs + """

            public interface IConsumer<T> where T : class
            {
                System.Threading.Tasks.Task Consume(T message);
            }

            internal sealed record OrderAccepted(System.Guid OrderId);

            internal class OrderConsumer : IConsumer<OrderAccepted>
            {
                public System.Threading.Tasks.Task Consume(OrderAccepted message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .VerifyNoIssues();

    // A same-named Publish on an unrelated, hand-rolled bus is not messaging either.
    [TestMethod]
    public void MessageContractMustBePublic_CompliantForUnrelatedOwnPublishOfInternalContract() =>
        builder.AddSnippet(
            Stubs + """

            public class OwnBus
            {
                public System.Threading.Tasks.Task Publish<T>(T message) where T : class => System.Threading.Tasks.Task.CompletedTask;
            }

            internal sealed record OrderAccepted(System.Guid OrderId);

            public class OrderService
            {
                private readonly OwnBus _bus;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _bus.Publish(new OrderAccepted(id));
            }
            """)
            .VerifyNoIssues();

    // A private nested contract is just as unreachable as an internal one.
    [TestMethod]
    public void MessageContractMustBePublic_NoncompliantForPrivateNestedContract() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                private sealed record OrderAccepted(System.Guid OrderId);

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new OrderAccepted(id)); // Noncompliant {{'OrderAccepted' is not public, so no other service can reference this contract.}}
            }
            """)
            .Verify();
}
