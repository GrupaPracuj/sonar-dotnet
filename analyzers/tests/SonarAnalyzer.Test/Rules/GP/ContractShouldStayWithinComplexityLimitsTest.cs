using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldStayWithinComplexityLimitsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldStayWithinComplexityLimits>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_NoncompliantForTooManyProperties() =>
        CreateBuilder(maxProperties: 3)
            .AddSnippet(
            """
            public sealed class OrderAcceptedContract // Noncompliant {{'OrderAcceptedContract' exceeds a message contract limit: 4 properties, above the limit of 3.}}
            {
                public string A { get; init; }
                public string B { get; init; }
                public string C { get; init; }
                public string D { get; init; }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_NoncompliantForTooDeepNesting() =>
        CreateBuilder(maxDepth: 2)
            .AddSnippet(
            """
            public sealed class LevelThreeContract
            {
                public string Value { get; init; }
            }

            public sealed class LevelTwoContract
            {
                public LevelThreeContract Next { get; init; }
            }

            public sealed class LevelOneContract
            {
                public LevelTwoContract Next { get; init; }
            }

            public sealed class OrderAcceptedContract // Noncompliant {{'OrderAcceptedContract' exceeds a message contract limit: contract types nested 3 levels deep, above the limit of 2.}}
            {
                public LevelOneContract Next { get; init; }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_CompliantWithinDefaultLimits() =>
        builder.AddSnippet(
            """
            public sealed record OrderLineContract(string Sku, int Quantity);

            public sealed record OrderAcceptedContract(
                System.Guid OrderId,
                string CustomerReference,
                decimal Total,
                string Currency,
                System.Collections.Generic.IReadOnlyList<OrderLineContract> Lines,
                System.DateTimeOffset OccurredAt);
            """)
            .VerifyNoIssues();

    // BCL types are not a nesting level, so a contract of strings and dates is depth zero however many it has.
    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_CompliantForBclMembersOnly() =>
        CreateBuilder(maxDepth: 1)
            .AddSnippet(
            """
            public sealed record OrderAcceptedContract(
                System.Guid OrderId,
                string CustomerReference,
                System.DateTimeOffset OccurredAt,
                System.Collections.Generic.IReadOnlyList<string> Tags);
            """)
            .VerifyNoIssues();

    // A self-referencing contract must not send the depth walk into infinite recursion.
    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_CompliantForSelfReferencingContract() =>
        builder.AddSnippet(
            """
            public sealed class CategoryContract
            {
                public string Name { get; init; }
                public CategoryContract Parent { get; init; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_CompliantForNonContractType() =>
        CreateBuilder(maxProperties: 1)
            .AddSnippet(
            """
            public sealed class OrderProjection
            {
                public string A { get; init; }
                public string B { get; init; }
            }
            """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilder(int maxProperties = 30, int maxDepth = 4, int maxComplexTypes = 10) =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.ContractShouldStayWithinComplexityLimits
            {
                MaxProperties = maxProperties,
                MaxDepth = maxDepth,
                MaxComplexTypes = maxComplexTypes,
            })
            .WithOptions(LanguageOptions.CSharpLatest);
}
