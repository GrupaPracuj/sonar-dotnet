using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractCollectionsShouldBeStableTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractCollectionsShouldBeStable>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForEnumerable() =>
        builder.AddSnippet(
            """
            public class OrderLine { }

            public sealed class OrderAcceptedContract
            {
                public System.Collections.Generic.IEnumerable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' is a lazy sequence - the serializer would enumerate it; use IReadOnlyList<T>.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForQueryable() =>
        builder.AddSnippet(
            """
            public class OrderLine { }

            public sealed class OrderAcceptedEvent
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' is a lazy sequence - the serializer would enumerate it; use IReadOnlyList<T>.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForRecordParameter() =>
        builder.AddSnippet(
            """
            public class OrderLine { }

            public sealed record OrderAcceptedContract(System.Collections.Generic.IEnumerable<OrderLine> Lines); // Noncompliant@-0 {{'Lines' is a lazy sequence - the serializer would enumerate it; use IReadOnlyList<T>.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_CompliantForReadOnlyList() =>
        builder.AddSnippet(
            """
            public class OrderLine { }

            public sealed record OrderAcceptedContract(
                System.Collections.Generic.IReadOnlyList<OrderLine> Lines,
                System.Collections.Generic.IReadOnlyCollection<string> Tags,
                OrderLine[] Extras);
            """)
            .VerifyNoIssues();

    // A string implements IEnumerable<char> but is not a lazy sequence, so the declared type is what matters.
    [TestMethod]
    public void ContractCollectionsShouldBeStable_CompliantForString() =>
        builder.AddSnippet(
            """
            public sealed record OrderAcceptedContract(string Reference);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_CompliantForNonContractType() =>
        builder.AddSnippet(
            """
            public class OrderLine { }

            public class OrderQuery
            {
                public System.Collections.Generic.IEnumerable<OrderLine> Lines { get; init; }
            }
            """)
            .VerifyNoIssues();
}
