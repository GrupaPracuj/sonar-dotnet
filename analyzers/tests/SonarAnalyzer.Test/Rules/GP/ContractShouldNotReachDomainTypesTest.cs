using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotReachDomainTypesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotReachDomainTypes>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace System.ComponentModel.DataAnnotations
        {
            public class KeyAttribute : System.Attribute { }
        }

        public class Customer
        {
            [System.ComponentModel.DataAnnotations.Key]
            public int Id { get; set; }
        }
        """;

    [TestMethod]
    public void ContractShouldNotReachDomainTypes_NoncompliantForDirectDomainMember() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record CustomerSummaryContract(string Name, Customer Entity); // Noncompliant@-0 {{'Entity' lets this contract reach the domain type 'Customer'.}}
            """)
            .Verify();

    // The case GP0043 and GP0057 both miss: the domain type is two hops away.
    [TestMethod]
    public void ContractShouldNotReachDomainTypes_NoncompliantThroughNestedContract() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class CustomerSummaryContract
            {
                public Customer Entity { get; init; } // Noncompliant {{'Entity' lets this contract reach the domain type 'Customer'.}}
            }

            public sealed class OrderAcceptedContract
            {
                public CustomerSummaryContract Customer { get; init; } // Noncompliant {{'Customer' lets this contract reach the domain type 'Customer'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotReachDomainTypes_NoncompliantThroughCollection() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderAcceptedContract
            {
                public System.Collections.Generic.IReadOnlyList<Customer> Customers { get; init; } // Noncompliant {{'Customers' lets this contract reach the domain type 'Customer'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotReachDomainTypes_CompliantForContractOwnedData() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record CustomerSummaryContract(string Name, System.Guid CustomerId);

            public sealed record OrderAcceptedContract(System.Guid OrderId, CustomerSummaryContract Customer);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotReachDomainTypes_NoncompliantForConfiguredDomainNamespace() =>
        CreateBuilder(domainNamespaces: "MyCompany.Domain")
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Domain
            {
                public class Money { }
            }

            public sealed record OrderAcceptedContract(System.Guid OrderId, MyCompany.Domain.Money Total); // Noncompliant@-0 {{'Total' lets this contract reach the domain type 'Money'.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotReachDomainTypes_CompliantForNonContractType() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderProjection
            {
                public Customer Entity { get; init; }
            }
            """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilder(string entityBaseTypes = "", string domainNamespaces = "") =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.ContractShouldNotReachDomainTypes { EntityBaseTypes = entityBaseTypes, DomainNamespaces = domainNamespaces })
            .WithOptions(LanguageOptions.CSharpLatest);
}
