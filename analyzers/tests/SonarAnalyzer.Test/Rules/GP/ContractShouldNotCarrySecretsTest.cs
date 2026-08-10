using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotCarrySecretsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotCarrySecrets>()
        .WithOptions(LanguageOptions.CSharpLatest);
    private readonly VerifierBuilder contractAssembly = new VerifierBuilder()
        .AddAnalyzer(() => new CS.ContractShouldNotCarrySecrets { ContractAssemblyNames = "project0" })
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractShouldNotCarrySecrets_NoncompliantForProperty() =>
        contractAssembly.AddSnippet(
            """
            public class ServiceRegisteredEvent
            {
                public string ServiceName { get; set; }
                public string ApiKey { get; set; } // Noncompliant {{'ApiKey' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_NoncompliantForPositionalRecord() =>
        contractAssembly.AddSnippet(
            """
            public sealed record ServiceRegisteredContract(string ServiceName, string ClientSecret); // Noncompliant@-0 {{'ClientSecret' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_NoncompliantForConnectionString() =>
        contractAssembly.AddSnippet(
            """
            public class DatabaseProvisionedMessage
            {
                public string ConnectionString { get; set; } // Noncompliant {{'ConnectionString' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.}}
            }
            """)
            .Verify();

    // A name that only points at a secret is the recommended fix, so it must not be reported.
    [TestMethod]
    public void ContractShouldNotCarrySecrets_CompliantForPointersToSecrets() =>
        contractAssembly.AddSnippet(
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
        builder.AddSnippet(
            """
            namespace GP.Juno.Abstractions.EventStream
            {
                public interface IPublisher
                {
                    System.Threading.Tasks.Task Publish<T>(T message) where T : class;
                }
            }

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
        contractAssembly
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
            .Verify();
}
