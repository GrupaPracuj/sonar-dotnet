using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotExposeNonSerializableMembersTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotExposeNonSerializableMembers>();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForStream() =>
        builder.AddSnippet(
            """
            using System.IO;

            public class UploadRequest
            {
                public Stream File { get; set; } // Noncompliant {{'File' has type 'System.IO.Stream', which does not serialize to JSON meaningfully - remove it from this contract.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForTask() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public class OrderResponse
            {
                public Task Processing { get; set; } // Noncompliant {{'Processing' has type 'System.Threading.Tasks.Task', which does not serialize to JSON meaningfully - remove it from this contract.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForDelegate() =>
        builder.AddSnippet(
            """
            using System;

            public class OrderContract
            {
                public Action Callback { get; set; } // Noncompliant {{'Callback' has type 'System.Action', which does not serialize to JSON meaningfully - remove it from this contract.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForPublicField() =>
        builder.AddSnippet(
            """
            using System.IO;

            public class UploadRequest
            {
                public Stream File; // Noncompliant {{'File' has type 'System.IO.Stream', which does not serialize to JSON meaningfully - remove it from this contract.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_CompliantForStaticField() =>
        builder.AddSnippet(
            """
            using System.IO;

            public class UploadRequest
            {
                public static readonly Stream Empty = null;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_CompliantForPrivateField() =>
        builder.AddSnippet(
            """
            using System.IO;

            public class UploadRequest
            {
                private Stream _file;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_CompliantForOrdinaryProperties() =>
        builder.AddSnippet(
            """
            public class OrderDto
            {
                public string Id { get; set; }
                public int Quantity { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_CompliantForNonContractType() =>
        builder.AddSnippet(
            """
            using System.IO;

            public class UploadHelper
            {
                public Stream File { get; set; }
            }
            """)
            .VerifyNoIssues();
}
