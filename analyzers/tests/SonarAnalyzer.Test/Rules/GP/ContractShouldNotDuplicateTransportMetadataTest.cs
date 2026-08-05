using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotDuplicateTransportMetadataTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotDuplicateTransportMetadata>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_NoncompliantForMessageId() =>
        builder.AddSnippet(
            """
            public sealed record OrderAcceptedContract(System.Guid OrderId, System.Guid MessageId); // Noncompliant@-0 {{'MessageId' duplicates transport metadata - read it from the consume context instead.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_NoncompliantForProperties() =>
        builder.AddSnippet(
            """
            public class OrderAcceptedEvent
            {
                public System.Guid OrderId { get; init; }
                public System.Guid ConversationId { get; init; } // Noncompliant {{'ConversationId' duplicates transport metadata - read it from the consume context instead.}}
                public System.DateTimeOffset SentTime { get; init; } // Noncompliant {{'SentTime' duplicates transport metadata - read it from the consume context instead.}}
                public string SourceAddress { get; init; } // Noncompliant {{'SourceAddress' duplicates transport metadata - read it from the consume context instead.}}
            }
            """)
            .Verify();

    // A domain identifier is named after what it identifies, so it does not collide with the metadata names.
    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_CompliantForDomainIdentifiers() =>
        builder.AddSnippet(
            """
            public sealed record OrderAcceptedContract(
                System.Guid OrderId,
                System.Guid ProcessId,
                string CustomerReference,
                System.DateTimeOffset OccurredAt);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_CompliantForNonContractType() =>
        builder.AddSnippet(
            """
            public class InboxRecord
            {
                public System.Guid MessageId { get; set; }
                public System.DateTimeOffset SentTime { get; set; }
            }
            """)
            .VerifyNoIssues();
}
