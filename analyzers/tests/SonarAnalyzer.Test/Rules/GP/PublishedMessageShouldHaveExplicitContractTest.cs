using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PublishedMessageShouldHaveExplicitContractTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PublishedMessageShouldHaveExplicitContract>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T @event) where T : class;
                System.Threading.Tasks.Task Publish(object @event);
            }
        }

        public sealed record OrderAccepted(System.Guid OrderId);
        """;

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_NoncompliantForAnonymousType() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new { OrderId = id }); // Noncompliant {{Publish a declared contract type instead of an anonymous type.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_NoncompliantForDictionary() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new System.Collections.Generic.Dictionary<string, object> { ["orderId"] = id }); // Noncompliant {{Publish a declared contract type instead of a loose dictionary.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_NoncompliantForObject() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(object payload) =>
                    _publisher.Publish(payload); // Noncompliant {{Publish a declared contract type instead of 'object'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_CompliantForDeclaredContract() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new OrderAccepted(id));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_CompliantForNonMessagingCall() =>
        builder.AddSnippet(
            Stubs + """

            public class Recorder
            {
                public void Publish(object payload) { }

                public void Record(System.Guid id) => Publish(new { OrderId = id });
            }
            """)
            .VerifyNoIssues();
}
