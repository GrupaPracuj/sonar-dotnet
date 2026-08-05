using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

// GP0068, GP0069 and GP0070 all key off "this enum is exposed by a contract", so they share their fixtures.
[TestClass]
public class ContractEnumRulesTest
{
    private readonly VerifierBuilder unknownValue = new VerifierBuilder<CS.ContractEnumShouldHaveUnknownValue>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private readonly VerifierBuilder explicitValues = new VerifierBuilder<CS.ContractEnumShouldAssignExplicitValues>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private readonly VerifierBuilder noFlags = new VerifierBuilder<CS.ContractEnumShouldNotBeFlags>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractEnumShouldHaveUnknownValue_NoncompliantWithoutZeroUnknown() =>
        unknownValue.AddSnippet(
            """
            public enum OrderStatus // Noncompliant {{'OrderStatus' is exposed by a contract but has no zero value named Unknown - a consumer cannot represent a value it does not recognise.}}
            {
                Pending = 1,
                Accepted = 2,
            }

            public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
            """)
            .Verify();

    // A correctly named member that is not at zero does not help: zero is where an unrecognised value lands.
    [TestMethod]
    public void ContractEnumShouldHaveUnknownValue_NoncompliantWhenUnknownIsNotZero() =>
        unknownValue.AddSnippet(
            """
            public enum OrderStatus // Noncompliant {{'OrderStatus' is exposed by a contract but has no zero value named Unknown - a consumer cannot represent a value it does not recognise.}}
            {
                Pending = 0,
                Unknown = 99,
            }

            public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
            """)
            .Verify();

    [TestMethod]
    public void ContractEnumShouldHaveUnknownValue_CompliantWithZeroUnknown() =>
        unknownValue.AddSnippet(
            """
            public enum OrderStatus
            {
                Unknown = 0,
                Pending = 1,
            }

            public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
            """)
            .VerifyNoIssues();

    // Nullable and collection wrappers still put the enum on the wire.
    [TestMethod]
    public void ContractEnumShouldHaveUnknownValue_NoncompliantThroughCollectionMember() =>
        unknownValue.AddSnippet(
            """
            public enum OrderStatus // Noncompliant {{'OrderStatus' is exposed by a contract but has no zero value named Unknown - a consumer cannot represent a value it does not recognise.}}
            {
                Pending = 1,
            }

            public sealed class OrderAcceptedContract
            {
                public System.Collections.Generic.IReadOnlyList<OrderStatus> History { get; init; }
            }
            """)
            .Verify();

    // An enum no contract exposes can be shaped freely.
    [TestMethod]
    public void ContractEnumShouldHaveUnknownValue_CompliantForInternalEnum() =>
        unknownValue.AddSnippet(
            """
            public enum ProcessingStage
            {
                Started = 1,
                Finished = 2,
            }

            public sealed class OrderProcessor
            {
                public ProcessingStage Stage { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractEnumShouldAssignExplicitValues_NoncompliantForImplicitValues() =>
        explicitValues.AddSnippet(
            """
            public enum OrderStatus // Noncompliant {{'OrderStatus' is exposed by a contract with implicit values - reordering or inserting a member would silently change what is already on the wire.}}
            {
                Unknown,
                Pending,
            }

            public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
            """)
            .Verify();

    // One implicit member is enough - it still moves when its predecessor changes.
    [TestMethod]
    public void ContractEnumShouldAssignExplicitValues_NoncompliantForPartiallyExplicitValues() =>
        explicitValues.AddSnippet(
            """
            public enum OrderStatus // Noncompliant {{'OrderStatus' is exposed by a contract with implicit values - reordering or inserting a member would silently change what is already on the wire.}}
            {
                Unknown = 0,
                Pending,
            }

            public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
            """)
            .Verify();

    [TestMethod]
    public void ContractEnumShouldAssignExplicitValues_CompliantForExplicitValues() =>
        explicitValues.AddSnippet(
            """
            public enum OrderStatus
            {
                Unknown = 0,
                Pending = 1,
            }

            public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractEnumShouldNotBeFlags_NoncompliantForFlagsEnum() =>
        noFlags.AddSnippet(
            """
            [System.Flags]
            public enum NotificationChannels // Noncompliant {{'NotificationChannels' is a flags enum exposed by a contract - a combined value carries bits a consumer may not recognise, and it cannot report that.}}
            {
                None = 0,
                Email = 1,
                Sms = 2,
            }

            public sealed record NotificationRequestedContract(System.Guid UserId, NotificationChannels Channels);
            """)
            .Verify();

    [TestMethod]
    public void ContractEnumShouldNotBeFlags_CompliantForPlainEnumCollection() =>
        noFlags.AddSnippet(
            """
            public enum NotificationChannel
            {
                Unknown = 0,
                Email = 1,
            }

            public sealed record NotificationRequestedContract(
                System.Guid UserId,
                System.Collections.Generic.IReadOnlyList<NotificationChannel> Channels);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractEnumShouldNotBeFlags_CompliantForInternalFlagsEnum() =>
        noFlags.AddSnippet(
            """
            [System.Flags]
            public enum ProcessingOptions
            {
                None = 0,
                Retry = 1,
            }

            public sealed class OrderProcessor
            {
                public ProcessingOptions Options { get; set; }
            }
            """)
            .VerifyNoIssues();
}
