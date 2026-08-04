using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PublishedEventShouldCarryOccurrenceTimeTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PublishedEventShouldCarryOccurrenceTime>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        public class AppConfig
        {
            public AppConfig Publishes<T>() => this;
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
    public void PublishedEventShouldCarryOccurrenceTime_NoncompliantForRegistration() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid PaymentId, decimal Amount);

            public static class Startup
            {
                public static AppConfig Register(AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>(); // Noncompliant {{'PaymentReceived' is published as an event but does not state when it occurred - add a DateTimeOffset OccurredAt.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedEventShouldCarryOccurrenceTime_NoncompliantForPublishCall() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid PaymentId, decimal Amount);

            public class PaymentService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Record(PaymentReceived @event) =>
                    _publisher.Publish(@event); // Noncompliant {{'PaymentReceived' is published as an event but does not state when it occurred - add a DateTimeOffset OccurredAt.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedEventShouldCarryOccurrenceTime_CompliantWithOccurredAt() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid PaymentId, decimal Amount, System.DateTimeOffset OccurredAt);

            public static class Startup
            {
                public static AppConfig Register(AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>();
            }
            """)
            .VerifyNoIssues();

    // DateTime is not enough - an instant crossing a service boundary needs its offset, as S6566 also requires.
    [TestMethod]
    public void PublishedEventShouldCarryOccurrenceTime_NoncompliantForDateTime() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid PaymentId, System.DateTime OccurredAt);

            public static class Startup
            {
                public static AppConfig Register(AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>(); // Noncompliant {{'PaymentReceived' is published as an event but does not state when it occurred - add a DateTimeOffset OccurredAt.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedEventShouldCarryOccurrenceTime_CompliantForConfiguredName() =>
        CreateBuilderWithNames("RecordedAt")
            .AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid PaymentId, System.DateTimeOffset RecordedAt);

            public static class Startup
            {
                public static AppConfig Register(AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>();
            }
            """)
            .VerifyNoIssues();

    // A contract that is never published is not reported.
    [TestMethod]
    public void PublishedEventShouldCarryOccurrenceTime_CompliantWhenNotPublished() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid PaymentId, decimal Amount);
            """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilderWithNames(string occurrenceTimeNames) =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.PublishedEventShouldCarryOccurrenceTime { OccurrenceTimeNames = occurrenceTimeNames })
            .WithOptions(LanguageOptions.CSharpLatest);
}
