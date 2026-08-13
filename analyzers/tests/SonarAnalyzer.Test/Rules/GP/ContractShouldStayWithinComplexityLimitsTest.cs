using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldStayWithinComplexityLimitsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldStayWithinComplexityLimits>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string MvcStub =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public abstract class ControllerBase { }
            public class ActionResult<T> { }
        }
        """;

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_NoncompliantForTooManyProperties() =>
        CreateBuilder(maxProperties: 3)
            .AddSnippet(
            """
            namespace Contracts;

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
            namespace Contracts;

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
    public void ContractShouldStayWithinComplexityLimits_NoncompliantForDepthThroughDictionaryValue() =>
        CreateBuilder(maxDepth: 1)
            .AddSnippet(
            """
            namespace Contracts;

            public sealed class Leaf
            {
                public string Value { get; init; }
            }

            public sealed class Branch
            {
                public Leaf Next { get; init; }
            }

            public sealed class OrderAcceptedContract // Noncompliant {{'OrderAcceptedContract' exceeds a message contract limit: contract types nested 2 levels deep, above the limit of 1.}}
            {
                public System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyDictionary<string, Branch>> Branches { get; init; }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_NoncompliantForReachableTypesThroughDictionaryValue() =>
        CreateBuilder(maxDepth: 5, maxComplexTypes: 1)
            .AddSnippet(
            """
            namespace Contracts;

            public sealed class Leaf
            {
                public string Value { get; init; }
            }

            public sealed class Branch
            {
                public Leaf Next { get; init; }
            }

            public sealed class OrderAcceptedContract // Noncompliant {{'OrderAcceptedContract' exceeds a message contract limit: 2 contract types reachable, above the limit of 1.}}
            {
                public System.Collections.Generic.IReadOnlyDictionary<string, Branch> Branches { get; init; }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_CompliantWithinDefaultLimits() =>
        builder.AddSnippet(
            """
            namespace Contracts;

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
            namespace Contracts;

            public sealed record OrderAcceptedContract(
                System.Guid OrderId,
                string CustomerReference,
                System.DateTimeOffset OccurredAt,
                System.Collections.Generic.IReadOnlyList<string> Tags);
            """)
            .VerifyNoIssues();

    // A shared subtype must be walked to its full depth on every path that reaches it, not just the first (possibly
    // shallower) one: BranchOne reaches Shared at depth 2, BranchTwo reaches the very same Shared at depth 3 and Shared
    // itself goes one level deeper through Deep, so the true nesting depth is 4 and must be reported even though the
    // first path to see Shared stops at 3.
    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_NoncompliantWhenSharedSubtypeIsReachedDeeperOnASecondPath() =>
        CreateBuilder(maxDepth: 3)
            .AddSnippet(
            """
            namespace Contracts;

            public sealed class Deep
            {
                public string Value { get; init; }
            }

            public sealed class Shared
            {
                public Deep Next { get; init; }
            }

            public sealed class BranchOne
            {
                public Shared Next { get; init; }
            }

            public sealed class MidBranchTwo
            {
                public Shared Next { get; init; }
            }

            public sealed class BranchTwo
            {
                public MidBranchTwo Next { get; init; }
            }

            public sealed class OrderAcceptedContract // Noncompliant {{'OrderAcceptedContract' exceeds a message contract limit: contract types nested 4 levels deep, above the limit of 3.}}
            {
                public BranchOne First { get; init; }
                public BranchTwo Second { get; init; }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_CompliantForRepeatedSharedSubtype() =>
        CreateBuilder(maxDepth: 5)
            .AddSnippet(
            """
            namespace Contracts;

            public sealed class Leaf
            {
                public string Value { get; init; }
            }

            public sealed class Shared
            {
                public Leaf One { get; init; }
                public Leaf Two { get; init; }
                public Leaf Three { get; init; }
                public Leaf Four { get; init; }
                public Leaf Five { get; init; }
            }

            public sealed class OrderAcceptedContract
            {
                public Shared One { get; init; }
                public Shared Two { get; init; }
                public Shared Three { get; init; }
                public Shared Four { get; init; }
                public Shared Five { get; init; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_NoncompliantWhenCachedPathContainsCycle() =>
        CreateBuilder(maxDepth: 3)
            .AddSnippet(
            """
            namespace Contracts;

            public sealed class Extra
            {
                public string Value { get; init; }
            }

            public sealed class X
            {
                public Shared Shared { get; init; }
                public Extra Extra { get; init; }
            }

            public sealed class Shared
            {
                public X Back { get; init; }
            }

            public sealed class Y
            {
                public Shared Shared { get; init; }
            }

            public sealed class OrderAcceptedContract // Noncompliant {{'OrderAcceptedContract' exceeds a message contract limit: contract types nested 4 levels deep, above the limit of 3.}}
            {
                public X First { get; init; }
                public Y Second { get; init; }
            }
            """)
            .Verify();

    // A self-referencing contract must not send the depth walk into infinite recursion.
    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_CompliantForSelfReferencingContract() =>
        builder.AddSnippet(
            """
            namespace Contracts;

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

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_CompliantForDatabaseDtoAndOutboundHttpRequestNames() =>
        CreateBuilder(maxProperties: 1)
            .AddSnippet(
            """
            namespace Database
            {
                internal sealed class CustomerDto
                {
                    public string FirstName { get; init; }
                    public string LastName { get; init; }
                }
            }

            namespace HttpClients.Surveys
            {
                internal sealed class CompositeRequest
                {
                    public string SurveyId { get; init; }
                    public string CustomerId { get; init; }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldStayWithinComplexityLimits_NoncompliantForControllerResponseOutsideContractsNamespace() =>
        CreateBuilder(maxProperties: 1)
            .AddSnippet(
            MvcStub + """

            public sealed class CustomerPayload // Noncompliant {{'CustomerPayload' exceeds a message contract limit: 2 properties, above the limit of 1.}}
            {
                public string FirstName { get; init; }
                public string LastName { get; init; }
            }

            public sealed class CustomersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<CustomerPayload> Get() => null;
            }
            """)
            .Verify();

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
