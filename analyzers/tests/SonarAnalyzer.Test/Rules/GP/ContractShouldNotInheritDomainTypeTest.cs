using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotInheritDomainTypeTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotInheritDomainType>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace System.ComponentModel.DataAnnotations
        {
            public class KeyAttribute : System.Attribute { }
        }

        public class Order
        {
            [System.ComponentModel.DataAnnotations.Key]
            public int Id { get; set; }
        }

        public interface IIntegrationEvent { }

        public class BaseContract { }
        """;

    [TestMethod]
    public void ContractShouldNotInheritDomainType_NoncompliantForEntityBase() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderAcceptedContract : Order // Noncompliant {{'Order' is a domain type - a contract that inherits it publishes the whole entity.}}
            {
                public System.DateTimeOffset OccurredAt { get; init; }
            }
            """)
            .Verify();

    // Inheriting another contract or implementing a marker is fine - only a domain base class is reported.
    [TestMethod]
    public void ContractShouldNotInheritDomainType_CompliantForContractBaseAndMarker() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderAcceptedContract : BaseContract, IIntegrationEvent
            {
                public System.Guid OrderId { get; init; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotInheritDomainType_CompliantForStandaloneRecord() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record OrderAcceptedContract(System.Guid OrderId, System.DateTimeOffset OccurredAt);
            """)
            .VerifyNoIssues();

    // A non-contract type inheriting an entity is ordinary domain modelling.
    [TestMethod]
    public void ContractShouldNotInheritDomainType_CompliantForNonContractType() =>
        builder.AddSnippet(
            Stubs + """

            public class SpecialOrder : Order
            {
            }
            """)
            .VerifyNoIssues();

    // A DbContext frequently lives in a separate persistence project rather than in the assembly being analyzed, so
    // the DbSet scan that recognises an entity has to reach into referenced assemblies too, not just the compilation
    // that is being analyzed.
    [TestMethod]
    public void ContractShouldNotInheritDomainType_NoncompliantForEntityDeclaredInReferencedAssembly()
    {
        var persistenceAssembly = new SnippetCompiler(
            """
            namespace Microsoft.EntityFrameworkCore
            {
                public class DbSet<TEntity> { }

                public class DbContext { }
            }

            public class Order
            {
                public int Id { get; set; }
            }

            public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
            }
            """).Compilation.ToMetadataReference();

        builder
            .AddReferences([persistenceAssembly])
            .AddSnippet(
            """
            public class OrderAcceptedContract : Order // Noncompliant {{'Order' is a domain type - a contract that inherits it publishes the whole entity.}}
            {
            }
            """)
            .Verify();
    }

    private static VerifierBuilder CreateBuilderWithConfiguration(string entityBaseTypes = "") =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.ContractShouldNotInheritDomainType { EntityBaseTypes = entityBaseTypes })
            .WithOptions(LanguageOptions.CSharpLatest);
}
