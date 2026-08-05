using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PublishedEventShouldCarryBusinessIdentifierTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PublishedEventShouldCarryBusinessIdentifier>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        public class AppConfig
        {
            public AppConfig Publishes<T>() => this;
        }
        """;

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_NoncompliantWithoutIdentifier() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(decimal Amount, string Currency, System.DateTimeOffset OccurredAt);

            public static class Startup
            {
                public static AppConfig Register(AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>(); // Noncompliant {{'PaymentReceived' carries no business identifier, so a consumer cannot tell what it is about.}}
            }
            """)
            .Verify();

    // MessageId identifies the message, not the thing it is about - a redelivery changes it for the same fact.
    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_NoncompliantForTransportIdentifierOnly() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid MessageId, decimal Amount);

            public static class Startup
            {
                public static AppConfig Register(AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>(); // Noncompliant {{'PaymentReceived' carries no business identifier, so a consumer cannot tell what it is about.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantWithIdentifier() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(System.Guid PaymentId, decimal Amount);

            public static class Startup
            {
                public static AppConfig Register(AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantWithReference() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(string CustomerReference, decimal Amount);

            public static class Startup
            {
                public static AppConfig Register(AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>();
            }
            """)
            .VerifyNoIssues();

    // A marker event is about the system, not a particular object, so there is nothing to identify.
    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantForMarkerEvent() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record MaintenanceModeEnabled();

            public static class Startup
            {
                public static AppConfig Register(AppConfig appConfig) =>
                    appConfig.Publishes<MaintenanceModeEnabled>();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantWhenNotPublished() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(decimal Amount);
            """)
            .VerifyNoIssues();

    // A team that names its keys differently can say so; the parameter replaces the defaults rather than extending them.
    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantForConfiguredSuffix() =>
        CreateBuilder("Ticket")
            .AddSnippet(
                Stubs + """

                public sealed record PaymentReceived(string PaymentTicket, decimal Amount);

                public static class Startup
                {
                    public static AppConfig Register(AppConfig appConfig) =>
                        appConfig.Publishes<PaymentReceived>();
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_NoncompliantWhenDefaultSuffixIsNoLongerConfigured() =>
        CreateBuilder("Ticket")
            .AddSnippet(
                Stubs + """

                public sealed record PaymentReceived(System.Guid PaymentId, decimal Amount);

                public static class Startup
                {
                    public static AppConfig Register(AppConfig appConfig) =>
                        appConfig.Publishes<PaymentReceived>(); // Noncompliant {{'PaymentReceived' carries no business identifier, so a consumer cannot tell what it is about.}}
                }
                """)
            .Verify();

    // An empty parameter switches the check off rather than reporting every event.
    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantWhenParameterIsEmpty() =>
        CreateBuilder(string.Empty)
            .AddSnippet(
                Stubs + """

                public sealed record PaymentReceived(decimal Amount);

                public static class Startup
                {
                    public static AppConfig Register(AppConfig appConfig) =>
                        appConfig.Publishes<PaymentReceived>();
                }
                """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilder(string identifierSuffixes) =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.PublishedEventShouldCarryBusinessIdentifier { IdentifierSuffixes = identifierSuffixes })
            .WithOptions(LanguageOptions.CSharpLatest);
}
