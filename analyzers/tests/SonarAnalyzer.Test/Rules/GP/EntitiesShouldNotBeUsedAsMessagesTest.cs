using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class EntitiesShouldNotBeUsedAsMessagesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.EntitiesShouldNotBeUsedAsMessages>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace System.ComponentModel.DataAnnotations
        {
            public class KeyAttribute : System.Attribute { }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbSet<TEntity> { }
        }

        namespace MassTransit
        {
            public interface IConsumer<T> where T : class
            {
                System.Threading.Tasks.Task Consume(T message);
            }
        }

        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T @event) where T : class;
            }
        }
        """;

    [TestMethod]
    public void EntitiesShouldNotBeUsedAsMessages_NoncompliantForEfAttributeEntity() =>
        builder.AddSnippet(
            Stubs + """

            public class Order
            {
                [System.ComponentModel.DataAnnotations.Key]
                public int Id { get; set; }
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(Order order) =>
                    _publisher.Publish(order); // Noncompliant {{'Order' is a database entity - use a dedicated contract type as the message instead.}}
            }
            """)
            .Verify();

    // Configured purely through DbSet<T>, with no attributes on the type at all.
    [TestMethod]
    public void EntitiesShouldNotBeUsedAsMessages_NoncompliantForDbSetEntity() =>
        builder.AddSnippet(
            Stubs + """

            public class Order
            {
                public int Id { get; set; }
            }

            public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(Order order) =>
                    _publisher.Publish(order); // Noncompliant {{'Order' is a database entity - use a dedicated contract type as the message instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EntitiesShouldNotBeUsedAsMessages_NoncompliantForConsumerOfEntity() =>
        builder.AddSnippet(
            Stubs + """

            public class Order
            {
                [System.ComponentModel.DataAnnotations.Key]
                public int Id { get; set; }
            }

            public class OrderConsumer : MassTransit.IConsumer<Order> // Noncompliant {{'Order' is a database entity - use a dedicated contract type as the message instead.}}
            {
                public System.Threading.Tasks.Task Consume(Order message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .Verify();

    // A same-named IConsumer<T> outside MassTransit (e.g. a MediatR-style handler interface) is not messaging, so
    // consuming an entity through it is not this rule's business.
    [TestMethod]
    public void EntitiesShouldNotBeUsedAsMessages_CompliantForUnrelatedOwnConsumerOfEntity() =>
        builder.AddSnippet(
            Stubs + """

            public interface IConsumer<T> where T : class
            {
                System.Threading.Tasks.Task Consume(T message);
            }

            public class Order
            {
                [System.ComponentModel.DataAnnotations.Key]
                public int Id { get; set; }
            }

            public class OrderConsumer : IConsumer<Order>
            {
                public System.Threading.Tasks.Task Consume(Order message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .VerifyNoIssues();

    // A same-named Publish on an unrelated, hand-rolled bus is not messaging either.
    [TestMethod]
    public void EntitiesShouldNotBeUsedAsMessages_CompliantForUnrelatedOwnPublishOfEntity() =>
        builder.AddSnippet(
            Stubs + """

            public class OwnBus
            {
                public System.Threading.Tasks.Task Publish<T>(T message) where T : class => System.Threading.Tasks.Task.CompletedTask;
            }

            public class Order
            {
                [System.ComponentModel.DataAnnotations.Key]
                public int Id { get; set; }
            }

            public class OrderService
            {
                private readonly OwnBus _bus;

                public System.Threading.Tasks.Task Accept(Order order) =>
                    _bus.Publish(order);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EntitiesShouldNotBeUsedAsMessages_CompliantForContractType() =>
        builder.AddSnippet(
            Stubs + """

            public class Order
            {
                [System.ComponentModel.DataAnnotations.Key]
                public int Id { get; set; }
            }

            public sealed class OrderAccepted
            {
                public System.Guid OrderId { get; }
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(Order order) =>
                    _publisher.Publish(new OrderAccepted());
            }
            """)
            .VerifyNoIssues();

    // Both convention-based signals are off by default, so a domain namespace alone is not enough until configured.
    [TestMethod]
    public void EntitiesShouldNotBeUsedAsMessages_CompliantForDomainNamespaceWithoutConfiguration() =>
        builder.AddSnippet(
            Stubs + """

            namespace MyCompany.Domain
            {
                public class Order
                {
                    public int Id { get; set; }
                }
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(MyCompany.Domain.Order order) =>
                    _publisher.Publish(order);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EntitiesShouldNotBeUsedAsMessages_NoncompliantForConfiguredDomainNamespace() =>
        CreateBuilderWithConfiguration(domainNamespaces: "MyCompany.Domain")
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Domain
            {
                public class Order
                {
                    public int Id { get; set; }
                }
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(MyCompany.Domain.Order order) =>
                    _publisher.Publish(order); // Noncompliant {{'Order' is a database entity - use a dedicated contract type as the message instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EntitiesShouldNotBeUsedAsMessages_NoncompliantForConfiguredEntityBaseType() =>
        CreateBuilderWithConfiguration(entityBaseTypes: "EntityBase")
            .AddSnippet(
            Stubs + """

            public abstract class EntityBase { }

            public class Order : EntityBase
            {
                public int Id { get; set; }
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(Order order) =>
                    _publisher.Publish(order); // Noncompliant {{'Order' is a database entity - use a dedicated contract type as the message instead.}}
            }
            """)
            .Verify();

    private static VerifierBuilder CreateBuilderWithConfiguration(string entityBaseTypes = "", string domainNamespaces = "") =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.EntitiesShouldNotBeUsedAsMessages { EntityBaseTypes = entityBaseTypes, DomainNamespaces = domainNamespaces })
            .WithOptions(LanguageOptions.CSharpLatest);
}
