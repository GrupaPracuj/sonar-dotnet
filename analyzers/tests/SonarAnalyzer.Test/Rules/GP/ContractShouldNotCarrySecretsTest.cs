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
public class ContractShouldNotCarrySecretsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotCarrySecrets>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string MessagingStub = """
        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T message) where T : class;
            }
        }
        """;

    [TestMethod]
    public void ContractShouldNotCarrySecrets_NoncompliantForProperty() =>
        builder
            .AddSnippet(MessagingStub)
            .AddSnippet(
            """
            public class ServiceRegisteredEvent
            {
                public string ServiceName { get; set; }
                public string ApiKey { get; set; } // Noncompliant {{'ApiKey' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.}}
            }

            public class Publisher(GP.Juno.Abstractions.EventStream.IPublisher publisher)
            {
                public System.Threading.Tasks.Task Publish(ServiceRegisteredEvent message) => publisher.Publish(message);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_NoncompliantForPositionalRecord() =>
        builder
            .AddSnippet(MessagingStub)
            .AddSnippet(
            """
            public sealed record ServiceRegisteredContract(string ServiceName, string ClientSecret); // Noncompliant@-0 {{'ClientSecret' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.}}

            public class Publisher(GP.Juno.Abstractions.EventStream.IPublisher publisher)
            {
                public System.Threading.Tasks.Task Publish(ServiceRegisteredContract message) => publisher.Publish(message);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_NoncompliantForConnectionString() =>
        builder
            .AddSnippet(MessagingStub)
            .AddSnippet(
            """
            public class DatabaseProvisionedMessage
            {
                public string ConnectionString { get; set; } // Noncompliant {{'ConnectionString' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.}}
            }

            public class Publisher(GP.Juno.Abstractions.EventStream.IPublisher publisher)
            {
                public System.Threading.Tasks.Task Publish(DatabaseProvisionedMessage message) => publisher.Publish(message);
            }
            """)
            .Verify();

    // A name that only points at a secret is the recommended fix, so it must not be reported.
    [TestMethod]
    public void ContractShouldNotCarrySecrets_CompliantForPointersToSecrets() =>
        builder
            .AddSnippet(MessagingStub)
            .AddSnippet(
            """
            public sealed record ServiceRegisteredContract(string ServiceName, string CredentialReference);

            public class ServiceRegisteredEvent
            {
                public string ServiceName { get; set; }
                public string ApiKeyId { get; set; }
                public string SecretUri { get; set; }
                public string TokenType { get; set; }
                public int PasswordLength { get; set; }
            }

            public class Publisher(GP.Juno.Abstractions.EventStream.IPublisher publisher)
            {
                public async System.Threading.Tasks.Task Publish(ServiceRegisteredContract contract, ServiceRegisteredEvent message)
                {
                    await publisher.Publish(contract);
                    await publisher.Publish(message);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_CompliantForSecretNamedFlags() =>
        builder
            .AddSnippet(MessagingStub)
            .AddSnippet(
                """
                public sealed record PaymentConfiguration(bool UseSandboxCredentials)
                {
                    public bool IncludeApiToken { get; init; }
                }

                public class Publisher(GP.Juno.Abstractions.EventStream.IPublisher publisher)
                {
                    public System.Threading.Tasks.Task Publish(PaymentConfiguration message) => publisher.Publish(message);
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_CompliantWithoutMessageContractEvidence() =>
        builder.AddSnippet(
            """
            public class SmtpOptions
            {
                public string Password { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_CompliantForHttpDto() =>
        builder.AddSnippet(
            """
            public sealed record LoginRequest(string UserName, string Password);

            public sealed record TokenResponse(string AccessToken);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_NoncompliantForPublishedTypeOutsideContractAssembly() =>
        builder
            .AddSnippet(MessagingStub)
            .AddSnippet(
            """
            public sealed record IntegrationPayload(string ApiKey); // Noncompliant@-0 {{'ApiKey' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.}}

            public sealed class Publisher
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher publisher;

                public System.Threading.Tasks.Task Publish(IntegrationPayload payload) =>
                    publisher.Publish(payload);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_ReportsMembersFromSeparatePartialDeclarations() =>
        builder
            .AddSnippet(MessagingStub)
            .AddSnippet(
                """
                public partial class Contract
                {
                    public string Password { get; set; } // Noncompliant
                }
                """)
            .AddSnippet(
                """
                public partial class Contract
                {
                    public string ApiToken { get; set; } // Noncompliant
                }
                """)
            .AddSnippet(
                """
                public class Publisher(GP.Juno.Abstractions.EventStream.IPublisher publisher)
                {
                    public System.Threading.Tasks.Task Publish(Contract message) => publisher.Publish(message);
                }
                """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_ReportsMetadataContractAtMessagingUse()
    {
        const string contractCode = """
            namespace Contracts;

            public sealed record SetPasswordRequest(string UserName, string Password);
            """;
        const string publisherCode = """
            namespace GP.Juno.Abstractions.EventStream
            {
                public interface IPublisher
                {
                    System.Threading.Tasks.Task Publish<T>(T message) where T : class;
                }
            }

            public class Publisher(GP.Juno.Abstractions.EventStream.IPublisher publisher)
            {
                public System.Threading.Tasks.Task Publish(Contracts.SetPasswordRequest message) =>
                    publisher.Publish(message); // Noncompliant {{'Password' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.}}
            }
            """;
        var compilation = SolutionBuilder.Create()
            .AddProject(AnalyzerLanguage.CSharp)
            .AddSnippet(contractCode)
            .Solution
            .AddProject(AnalyzerLanguage.CSharp)
            .AddProjectReference(x => x.ProjectIds[0])
            .AddSnippet(publisherCode)
            .GetCompilation();

        DiagnosticVerifier.Verify(compilation, [new CS.ContractShouldNotCarrySecrets()], CompilationErrorBehavior.Default, null, [], []);
    }
}
