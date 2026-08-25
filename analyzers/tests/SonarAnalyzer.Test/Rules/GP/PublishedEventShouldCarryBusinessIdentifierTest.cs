/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PublishedEventShouldCarryBusinessIdentifierTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PublishedEventShouldCarryBusinessIdentifier>()
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

        // An unrelated bus with a same-named registration method - not GP.Juno, so it must never be treated as one.
        public class OwnAppConfig
        {
            public OwnAppConfig Publishes<T>() => this;
        }

        namespace MassTransitSupport
        {
            public class AppConfig
            {
                public AppConfig Publishes<T>() => this;
            }
        }
        """;

    [DataTestMethod]
    // Natural keys, taken from contracts this rule used to report: an event about an email address is identified by
    // the address, and a replication by the GUID of the data being replicated.
    [DataRow("string EmailAddress, string SenderEmail")]
    [DataRow("string Email")]
    [DataRow("string UserEmail")]
    [DataRow("string Login")]
    [DataRow("string Username")]
    [DataRow("System.Guid FileDataGuid")]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantWithNaturalKey(string members) =>
        builder.AddSnippet(
            Stubs + $$"""

            public sealed record EmailAddressBlocked({{members}}, System.DateTimeOffset OccurredAt);

            public static class Startup
            {
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
                    appConfig.Publishes<EmailAddressBlocked>();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    // A name that merely mentions a category is not an identifier, so the suffix list must not swallow it.
    public void PublishedEventShouldCarryBusinessIdentifier_NoncompliantForCategoryNameOnly() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record EmailBounced(string BounceCategoryName, System.DateTimeOffset OccurredAt);

            public static class Startup
            {
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
                    appConfig.Publishes<EmailBounced>(); // Noncompliant {{'EmailBounced' carries no business identifier, so a consumer cannot tell what it is about.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_NoncompliantWithoutIdentifier() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(decimal Amount, string Currency, System.DateTimeOffset OccurredAt);

            public static class Startup
            {
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
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
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
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
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
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
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantWithNestedPayloadIdentifier() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record FileSource(System.Guid FileId);
            public sealed record CopyFileToCdn(FileSource Source, string DestinationPath, System.DateTimeOffset OccurredAt);

            public static class Startup
            {
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
                    appConfig.Publishes<CopyFileToCdn>();
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
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
                    appConfig.Publishes<MaintenanceModeEnabled>();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_NoncompliantWhenNestedPayloadHasNoIdentifier() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record FileSource(string FileName, long Size);
            public sealed record CopyFileToCdn(FileSource Source, string DestinationPath, System.DateTimeOffset OccurredAt);

            public static class Startup
            {
                public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
                    appConfig.Publishes<CopyFileToCdn>(); // Noncompliant {{'CopyFileToCdn' carries no business identifier, so a consumer cannot tell what it is about.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantWhenNotPublished() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(decimal Amount);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantForCommandPublishedThroughLegacyApi() =>
        builder.AddSnippet(
            Stubs + """

            namespace GP.Juno.Abstractions.EventStream
            {
                public interface IPublisher
                {
                    System.Threading.Tasks.Task Publish<T>(T message) where T : class;
                }
            }

            public sealed class StartMultiStepRegisterCommand
            {
                public string Email { get; init; }
                public string Group { get; init; }
            }

            public sealed class RegistrationService
            {
                public System.Threading.Tasks.Task Start(
                    GP.Juno.Abstractions.EventStream.IPublisher publisher,
                    StartMultiStepRegisterCommand command) =>
                    publisher.Publish(command);
            }
            """)
            .VerifyNoIssues();

    // A same-named Publishes<T> on an unrelated, non-GP.Juno registration surface is not messaging.
    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantForUnrelatedOwnPublishes() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(decimal Amount);

            public static class Startup
            {
                public static OwnAppConfig Register(OwnAppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedEventShouldCarryBusinessIdentifier_CompliantForNamespacePrefixLookAlike() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record PaymentReceived(decimal Amount);

            public static class Startup
            {
                public static MassTransitSupport.AppConfig Register(MassTransitSupport.AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>();
            }
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
                    public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
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
                    public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
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
                    public static GP.Juno.Configuration.AppConfig Register(GP.Juno.Configuration.AppConfig appConfig) =>
                        appConfig.Publishes<PaymentReceived>();
                }
                """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilder(string identifierSuffixes) =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.PublishedEventShouldCarryBusinessIdentifier { IdentifierSuffixes = identifierSuffixes })
            .WithOptions(LanguageOptions.CSharpLatest);
}
