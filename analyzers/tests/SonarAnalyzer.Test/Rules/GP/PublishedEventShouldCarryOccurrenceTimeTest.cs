using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PublishedEventShouldCarryOccurrenceTimeTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PublishedEventShouldCarryOccurrenceTime>()
        .WithOptions(LanguageOptions.CSharpLatest);

    // Shaped after GP.Juno's real registration surface (GP.Juno.Configuration.AppConfig, extended by
    // GP.Juno.EventStream.Api.AppConfigMessageDeclarationExtensions.Publishes<T>), so the rule is exercised through
    // the same reduced-extension-method call shape the real API uses.
    private const string Stubs =
        """
        using GP.Juno.EventStream.Api;

        namespace GP.Juno.Configuration
        {
            public interface AppConfig { }
        }

        namespace GP.Juno.EventStream.Api
        {
            public static class AppConfigMessageDeclarationExtensions
            {
                public static GP.Juno.Configuration.AppConfig Publishes<T>(this GP.Juno.Configuration.AppConfig appConfig) => appConfig;
            }
        }

        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T @event) where T : class;
            }
        }

        // An unrelated bus with a same-named registration method - not GP.Juno, so it must never be treated as one.
        public class OwnAppConfig
        {
            public OwnAppConfig Publishes<T>() => this;
        }
        """;

    [TestMethod]
    public void PublishedEventShouldCarryOccurrenceTime_NoncompliantForRegistration() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid PaymentId, decimal Amount);

            public static class Startup
            {
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>(); // Noncompliant {{'PaymentReceived' is published as an event but does not state when it occurred - add a DateTimeOffset OccurredAt.}}
            }
            """)
            .Verify();

    // A same-named Publishes<T> on an unrelated, non-GP.Juno registration surface is not messaging.
    [TestMethod]
    public void PublishedEventShouldCarryOccurrenceTime_CompliantForUnrelatedOwnPublishes() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid PaymentId, decimal Amount);

            public static class Startup
            {
                public static OwnAppConfig Register(OwnAppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>();
            }
            """)
            .VerifyNoIssues();

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
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
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
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
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
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
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
