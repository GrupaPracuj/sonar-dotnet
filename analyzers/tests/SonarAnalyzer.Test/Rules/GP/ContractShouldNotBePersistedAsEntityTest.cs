/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldNotBePersistedAsEntityTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotBePersistedAsEntity>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace System.ComponentModel.DataAnnotations.Schema
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

        namespace Contracts
        {
            public sealed record OrderAccepted(System.Guid OrderId);
        }

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
                public Microsoft.EntityFrameworkCore.DbSet<Contracts.OrderAccepted> AcceptedOrders { get; set; } // Noncompliant {{'OrderAccepted' is a message contract - persisting it makes the wire format and the schema the same thing.}}
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
                    modelBuilder.Entity<Contracts.OrderAccepted>(); // Noncompliant {{'OrderAccepted' is a message contract - persisting it makes the wire format and the schema the same thing.}}
            }
            """)
            .Verify();

    // Mapped through attributes rather than a DbSet.
    [TestMethod]
    public void ContractShouldNotBePersistedAsEntity_NoncompliantForMappingAttribute() =>
        builder.AddSnippet(
            Stubs + """

            namespace Contracts
            {
                [System.ComponentModel.DataAnnotations.Schema.Table("accepted_orders")]
                public sealed class MappedOrder // Noncompliant {{'MappedOrder' is a message contract - persisting it makes the wire format and the schema the same thing.}}
                {
                    public System.Guid OrderId { get; set; }
                }
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

    [TestMethod]
    public void ContractShouldNotBePersistedAsEntity_CompliantForContractSuffixAlone() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record CustomerResponse(System.Guid Id);

            public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public Microsoft.EntityFrameworkCore.DbSet<CustomerResponse> Customers { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotBePersistedAsEntity_NoncompliantForPublishedTypeOutsideContractsNamespace() =>
        builder.AddSnippet(
            Stubs + """

            namespace MassTransit
            {
                public interface IPublishEndpoint
                {
                    System.Threading.Tasks.Task Publish<T>(T message) where T : class;
                }
            }

            public sealed record OrderPublished(System.Guid OrderId);

            public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public Microsoft.EntityFrameworkCore.DbSet<OrderPublished> Orders { get; set; } // Noncompliant {{'OrderPublished' is a message contract - persisting it makes the wire format and the schema the same thing.}}
            }

            public class OrderService
            {
                private readonly MassTransit.IPublishEndpoint publisher;

                public System.Threading.Tasks.Task Publish(OrderPublished message) => publisher.Publish(message);
            }
            """)
            .Verify();

    // A custom attribute that happens to share a name with an EF mapping attribute (Table, Key, Column, ForeignKey,
    // PrimaryKey) but lives in a different namespace carries no EF semantics and must not trigger the rule.
    [TestMethod]
    public void ContractShouldNotBePersistedAsEntity_CompliantForLookAlikeAttributeInDifferentNamespace() =>
        builder.AddSnippet(
            Stubs + """

            namespace MyCompany.Annotations
            {
                public class TableAttribute : System.Attribute
                {
                    public TableAttribute(string name) { }
                }
            }

            [MyCompany.Annotations.Table("accepted_orders")]
            public sealed class OrderAcceptedEvent
            {
                public System.Guid OrderId { get; set; }
            }
            """)
            .VerifyNoIssues();
}
