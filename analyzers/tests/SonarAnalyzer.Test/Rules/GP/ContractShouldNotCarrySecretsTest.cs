using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotCarrySecretsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotCarrySecrets>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractShouldNotCarrySecrets_NoncompliantForProperty() =>
        builder.AddSnippet(
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
        builder.AddSnippet(
            """
            public sealed record ServiceRegisteredContract(string ServiceName, string ClientSecret); // Noncompliant@-0 {{'ClientSecret' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotCarrySecrets_NoncompliantForConnectionString() =>
        builder.AddSnippet(
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
        builder.AddSnippet(
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

    // Only types named like a contract are examined, so an internal options class is left alone.
    [TestMethod]
    public void ContractShouldNotCarrySecrets_CompliantForNonContractType() =>
        builder.AddSnippet(
            """
            public class SmtpOptions
            {
                public string Password { get; set; }
            }
            """)
            .VerifyNoIssues();
}
