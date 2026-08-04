using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotCarryBinaryPayloadTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotCarryBinaryPayload>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractShouldNotCarryBinaryPayload_NoncompliantForByteArray() =>
        builder.AddSnippet(
            """
            public sealed record DocumentUploadedContract(System.Guid DocumentId, byte[] FileContent); // Noncompliant@-0 {{'FileContent' puts binary content on the broker - publish a reference to it instead.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotCarryBinaryPayload_NoncompliantForByteCollectionProperty() =>
        builder.AddSnippet(
            """
            public class DocumentUploadedEvent
            {
                public System.Collections.Generic.IReadOnlyList<byte> Content { get; set; } // Noncompliant {{'Content' puts binary content on the broker - publish a reference to it instead.}}
            }
            """)
            .Verify();

    // A base64 string carries the same payload, so the name is the signal when the type is not.
    [TestMethod]
    public void ContractShouldNotCarryBinaryPayload_NoncompliantForBinaryNamedString() =>
        builder.AddSnippet(
            """
            public class DocumentUploadedEvent
            {
                public string AttachmentContent { get; set; } // Noncompliant {{'AttachmentContent' puts binary content on the broker - publish a reference to it instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotCarryBinaryPayload_CompliantForReference() =>
        builder.AddSnippet(
            """
            public sealed record DocumentUploadedContract(
                System.Guid DocumentId,
                System.Uri ContentUri,
                long SizeBytes,
                string ContentType,
                string Checksum);
            """)
            .VerifyNoIssues();

    // Stream is GP0025's case, on the stronger ground that it does not serialize at all.
    [TestMethod]
    public void ContractShouldNotCarryBinaryPayload_CompliantForStreamCoveredByGP0025() =>
        builder.AddSnippet(
            """
            public class DocumentUploadedRequest
            {
                public System.IO.Stream Content { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotCarryBinaryPayload_CompliantForNonContractType() =>
        builder.AddSnippet(
            """
            public class DocumentStorage
            {
                public byte[] FileContent { get; set; }
            }
            """)
            .VerifyNoIssues();
}
