using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractTypesShouldNotFormACycleTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractTypesShouldNotFormACycle>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractTypesShouldNotFormACycle_NoncompliantForDirectSelfReference() =>
        builder.AddSnippet(
            """
            public sealed class CategoryContract
            {
                public string Name { get; init; }
                public CategoryContract Parent { get; init; } // Noncompliant {{'Parent' lets 'CategoryContract' reach itself - the serializer throws on a circular reference.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractTypesShouldNotFormACycle_NoncompliantForMutualReferenceThroughCollection() =>
        builder.AddSnippet(
            """
            public sealed class OrderContract
            {
                // Both edges close the cycle, so both are reported - either one is a place to cut it.
                public System.Collections.Generic.IReadOnlyList<OrderLineContract> Lines { get; init; } // Noncompliant {{'Lines' lets 'OrderContract' reach itself - the serializer throws on a circular reference.}}
            }

            public sealed class OrderLineContract
            {
                public string Sku { get; init; }
                public OrderContract Order { get; init; } // Noncompliant {{'Order' lets 'OrderLineContract' reach itself - the serializer throws on a circular reference.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractTypesShouldNotFormACycle_NoncompliantForRecordParameter() =>
        builder.AddSnippet(
            """
            public sealed record CategoryContract(string Name, CategoryContract Parent); // Noncompliant@-0 {{'Parent' lets 'CategoryContract' reach itself - the serializer throws on a circular reference.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractTypesShouldNotFormACycle_CompliantForIdentifierInsteadOfBackReference() =>
        builder.AddSnippet(
            """
            public sealed class OrderContract
            {
                public System.Collections.Generic.IReadOnlyList<OrderLineContract> Lines { get; init; }
            }

            public sealed class OrderLineContract
            {
                public string Sku { get; init; }
                public System.Guid OrderId { get; init; }
            }
            """)
            .VerifyNoIssues();

    // A deep but acyclic graph is fine - depth is GP0062's concern, not this rule's.
    [TestMethod]
    public void ContractTypesShouldNotFormACycle_CompliantForDeepAcyclicGraph() =>
        builder.AddSnippet(
            """
            public sealed class CountryContract
            {
                public string Code { get; init; }
            }

            public sealed class AddressContract
            {
                public CountryContract Country { get; init; }
            }

            public sealed class CustomerContract
            {
                public AddressContract Address { get; init; }
            }

            public sealed class OrderContract
            {
                public CustomerContract Customer { get; init; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractTypesShouldNotFormACycle_CompliantForNonContractType() =>
        builder.AddSnippet(
            """
            public sealed class CategoryNode
            {
                public string Name { get; init; }
                public CategoryNode Parent { get; init; }
            }
            """)
            .VerifyNoIssues();
}
