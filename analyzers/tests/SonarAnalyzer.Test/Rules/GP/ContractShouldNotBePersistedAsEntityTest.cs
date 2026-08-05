using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotBePersistedAsEntityTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotBePersistedAsEntity>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace System.ComponentModel.DataAnnotations
        {
            public class TableAttribute : System.Attribute
            {
                public TableAttribute(string name) { }
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbSet<TEntity> { }

            public class ModelBuilder
            {
                public object Entity<TEntity>() => null;
            }

            public class DbContext { }
        }

        public sealed record OrderAcceptedContract(System.Guid OrderId);

        public class AcceptedOrderRecord
        {
            public int Id { get; set; }
        }
        """;

    [TestMethod]
    public void ContractShouldNotBePersistedAsEntity_NoncompliantForDbSetOfContract() =>
        builder.AddSnippet(
            Stubs + """

            public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public Microsoft.EntityFrameworkCore.DbSet<OrderAcceptedContract> AcceptedOrders { get; set; } // Noncompliant {{'OrderAcceptedContract' is a message contract - persisting it makes the wire format and the schema the same thing.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotBePersistedAsEntity_NoncompliantForEntityConfiguration() =>
        builder.AddSnippet(
            Stubs + """

            public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public void Configure(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder) =>
                    modelBuilder.Entity<OrderAcceptedContract>(); // Noncompliant {{'OrderAcceptedContract' is a message contract - persisting it makes the wire format and the schema the same thing.}}
            }
            """)
            .Verify();

    // Mapped through attributes rather than a DbSet.
    [TestMethod]
    public void ContractShouldNotBePersistedAsEntity_NoncompliantForMappingAttribute() =>
        builder.AddSnippet(
            Stubs + """

            [System.ComponentModel.DataAnnotations.Table("accepted_orders")]
            public sealed class OrderAcceptedEvent // Noncompliant {{'OrderAcceptedEvent' is a message contract - persisting it makes the wire format and the schema the same thing.}}
            {
                public System.Guid OrderId { get; set; }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotBePersistedAsEntity_CompliantForSeparateEntity() =>
        builder.AddSnippet(
            Stubs + """

            public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public Microsoft.EntityFrameworkCore.DbSet<AcceptedOrderRecord> AcceptedOrders { get; set; }
            }
            """)
            .VerifyNoIssues();
}
