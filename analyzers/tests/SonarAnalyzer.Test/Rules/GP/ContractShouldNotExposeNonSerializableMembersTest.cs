using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotExposeNonSerializableMembersTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotExposeNonSerializableMembers>()
        .WithOptions(LanguageOptions.CSharpLatest);

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
    public void ContractShouldNotExposeNonSerializableMembers_CompliantOutsideSerializedPropertySurface() =>
        builder.AddSnippet(
            """
            public class UploadRequest
            {
                public static System.IO.Stream Shared { get; }
                public System.IO.Stream WriteOnly { private get; set; }
                internal System.IO.Stream Internal { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_CompliantForKnownJsonIgnoreAttributes() =>
        builder.AddSnippet(
            """
            namespace System.Text.Json.Serialization
            {
                public sealed class JsonIgnoreAttribute : System.Attribute { }
            }

            namespace Newtonsoft.Json
            {
                public sealed class JsonIgnoreAttribute : System.Attribute { }
            }

            public class UploadRequest
            {
                [System.Text.Json.Serialization.JsonIgnore]
                public System.IO.Stream SystemTextJsonFile { get; set; }

                [Newtonsoft.Json.JsonIgnore]
                public System.IO.Stream NewtonsoftFile { get; set; }

                [Newtonsoft.Json.JsonIgnore]
                public System.IO.Stream IgnoredField;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForLookalikeJsonIgnoreAttribute() =>
        builder.AddSnippet(
            """
            public sealed class JsonIgnoreAttribute : System.Attribute { }

            public class UploadRequest
            {
                [JsonIgnore]
                public System.IO.Stream File { get; set; } // Noncompliant@-1 {{'File' has type 'System.IO.Stream', which does not serialize to JSON meaningfully - remove it from this contract.}}
            }
            """)
            .Verify();

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

    // The remaining CONTRACT004 types: live objects belonging to the current process and request.
    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForRuntimeObjects() =>
        builder.AddSnippet(
            """
            public class OrderRequest
            {
                public System.Exception Failure { get; set; } // Noncompliant {{'Failure' has type 'System.Exception', which does not serialize to JSON meaningfully - remove it from this contract.}}
                public System.Type Kind { get; set; } // Noncompliant {{'Kind' has type 'System.Type', which does not serialize to JSON meaningfully - remove it from this contract.}}
                public System.Threading.CancellationToken Cancellation { get; set; } // Noncompliant {{'Cancellation' has type 'System.Threading.CancellationToken', which does not serialize to JSON meaningfully - remove it from this contract.}}
            }
            """)
            .Verify();

    // Exception is almost always used through a derived type, so base classes are walked.
    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForDerivedException() =>
        builder.AddSnippet(
            """
            public class OrderRequest
            {
                public System.InvalidOperationException Failure { get; set; } // Noncompliant {{'Failure' has type 'System.InvalidOperationException', which does not serialize to JSON meaningfully - remove it from this contract.}}
            }
            """)
            .Verify();

    // Any delegate, not only Action and Func.
    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForCustomDelegate() =>
        builder.AddSnippet(
            """
            public delegate void OrderHandler(int id);

            public class OrderContract
            {
                public OrderHandler Handler { get; set; } // Noncompliant {{'Handler' has type 'OrderHandler', which does not serialize to JSON meaningfully - remove it from this contract.}}
            }
            """)
            .Verify();

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

    // A positional record parameter is the idiomatic way to declare a contract, so it has to be reported like a property.
    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForRecordParameter() =>
        builder.AddSnippet(
            """
            using System.IO;

            public sealed record UploadRequest(string FileName, Stream Content); // Noncompliant@-0 {{'Content' has type 'System.IO.Stream', which does not serialize to JSON meaningfully - remove it from this contract.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_CompliantForRecordParameterOfSerializableType() =>
        builder.AddSnippet(
            """
            public sealed record UploadRequest(string FileName, byte[] Content);
            """)
            .VerifyNoIssues();

    // A record struct is just as much a positional record as a record class, and must be checked the same way.
    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForRecordStructParameter() =>
        builder.AddSnippet(
            """
            using System.IO;

            public readonly record struct UploadRequest(string FileName, Stream Content); // Noncompliant@-0 {{'Content' has type 'System.IO.Stream', which does not serialize to JSON meaningfully - remove it from this contract.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_CompliantForRecordStructParameterOfSerializableType() =>
        builder.AddSnippet(
            """
            public readonly record struct UploadRequest(string FileName, byte[] Content);
            """)
            .VerifyNoIssues();

    // An event or a command is as much a contract as a request is, so the same member is reported there too.
    [TestMethod]
    public void ContractShouldNotExposeNonSerializableMembers_NoncompliantForEventNamedContract() =>
        builder.AddSnippet(
            """
            using System.IO;

            public sealed class OrderAcceptedEvent
            {
                public Stream Payload { get; set; } // Noncompliant {{'Payload' has type 'System.IO.Stream', which does not serialize to JSON meaningfully - remove it from this contract.}}
            }
            """)
            .Verify();
}
