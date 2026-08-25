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

    [DataTestMethod]
    // The base-type path used to be off unless a project set entityBaseTypes, which left only EF attributes and DbSet
    // membership - both needing the entity in the same compilation as the contract.
    [DataRow("Entity")]
    [DataRow("EntityBase")]
    [DataRow("AggregateRoot")]
    [DataRow("AggregateRootBase")]
    [DataRow("DomainEntity")]
    public void ContractShouldNotInheritDomainType_NoncompliantForDefaultEntityBaseTypes(string baseType) =>
        builder.AddSnippet(
            (Stubs + """

            public class BASE { }

            namespace Contracts
            {
                public sealed class OrderAccepted : global::BASE { } // Noncompliant {{'BASE' is a domain type - a contract that inherits it publishes the whole entity.}}
            }
            """).Replace("BASE", baseType))
            .Verify();

    [TestMethod]
    public void ContractShouldNotInheritDomainType_NoncompliantForEntityBase() =>
        builder.AddSnippet(
            Stubs + """

            namespace Contracts
            {
                public class OrderAccepted : global::Order // Noncompliant {{'Order' is a domain type - a contract that inherits it publishes the whole entity.}}
                {
                    public System.DateTimeOffset OccurredAt { get; init; }
                }
            }
            """)
            .Verify();

    // Inheriting another contract or implementing a marker is fine - only a domain base class is reported.
    [TestMethod]
    public void ContractShouldNotInheritDomainType_CompliantForContractBaseAndMarker() =>
        builder.AddSnippet(
            Stubs + """

            namespace Contracts
            {
                public class OrderAccepted : global::BaseContract, global::IIntegrationEvent
                {
                    public System.Guid OrderId { get; init; }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotInheritDomainType_CompliantForStandaloneRecord() =>
        builder.AddSnippet(
            Stubs + """

            namespace Contracts
            {
                public sealed record OrderAccepted(System.Guid OrderId, System.DateTimeOffset OccurredAt);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotInheritDomainType_CompliantForResponseSuffixAlone() =>
        builder.AddSnippet(
            Stubs + """

            public class ViewResponse : Order
            {
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotInheritDomainType_NoncompliantForPublishedTypeOutsideContractsNamespace() =>
        builder.AddSnippet(
            Stubs + """

            namespace MassTransit
            {
                public interface IPublishEndpoint
                {
                    System.Threading.Tasks.Task Publish<T>(T message) where T : class;
                }
            }

            public class OrderAccepted : Order // Noncompliant {{'Order' is a domain type - a contract that inherits it publishes the whole entity.}}
            {
            }

            public class OrderService
            {
                private readonly MassTransit.IPublishEndpoint publisher;

                public System.Threading.Tasks.Task Publish(OrderAccepted message) => publisher.Publish(message);
            }
            """)
            .Verify();

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

    [TestMethod]
    public void ContractShouldNotInheritDomainType_CompliantForCustomKeyAttribute() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class KeyAttribute : System.Attribute { }

            public class ViewModel
            {
                [Key]
                public int Id { get; set; }
            }

            public class ViewResponse : ViewModel
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
            namespace Contracts
            {
                public class OrderAccepted : global::Order // Noncompliant {{'Order' is a domain type - a contract that inherits it publishes the whole entity.}}
                {
                }
            }
            """)
            .Verify();
    }

    private static VerifierBuilder CreateBuilderWithConfiguration(string entityBaseTypes = "") =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.ContractShouldNotInheritDomainType { EntityBaseTypes = entityBaseTypes })
            .WithOptions(LanguageOptions.CSharpLatest);
}
