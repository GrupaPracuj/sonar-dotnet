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
public class ContractCollectionsShouldBeStableTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractCollectionsShouldBeStable>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string MessagingStub =
        """
        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T message) where T : class;
            }
        }
        """;

    private const string MvcStub =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok(object value) => null;
            }
            public class ActionResult<T> { }
        }
        """;

    private const string MinimalApiStub =
        """
        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static void MapGet<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }

            public static class Results
            {
                public static IResult Ok<T>(T value) => null;
            }
        }
        """;

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForContractsNamespace() =>
        builder.AddSnippet(
            """
            namespace GP.Orders.Contracts
            {
                public class OrderLine { }

                public sealed class OrderPayload
                {
                    public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForPublishedType() =>
        builder.AddSnippet(
            MessagingStub + """

            public class OrderLine { }

            public sealed class OrderPayload
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher publisher;

                public System.Threading.Tasks.Task Publish(OrderPayload payload) => publisher.Publish(payload);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForControllerResponseType() =>
        builder.AddSnippet(
            MvcStub + """

            public class OrderLine { }

            public sealed class OrderPayload
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<OrderPayload>> Get() => null;
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForWrappedControllerResponseCollectionItem() =>
        builder.AddSnippet(
            MvcStub + """

            public class OrderLine { }

            public sealed class OrderPayload
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Threading.Tasks.ValueTask<Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<OrderPayload>>> Get() => default;
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForMvcResponsePayload() =>
        builder.AddSnippet(
            MvcStub + """

            public class OrderLine { }

            public sealed class OrderPayload
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(new OrderPayload());
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForMinimalApiResponsePayload() =>
        builder.AddSnippet(
            MinimalApiStub + """

            public class OrderLine { }

            public sealed class OrderPayload
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}
            }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(
                        app,
                        "/orders",
                        () => Microsoft.AspNetCore.Http.Results.Ok(new OrderPayload()));
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForDirectMinimalApiResponse() =>
        builder.AddSnippet(
            MinimalApiStub + """

            public class OrderLine { }

            public sealed class OrderPayload
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}
            }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(
                        app,
                        "/orders",
                        () => new OrderPayload());
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForPublishedRecordParameter() =>
        builder.AddSnippet(
            MessagingStub + """

            public class OrderLine { }

            public sealed record OrderPayload(
                System.Linq.IQueryable<OrderLine> Lines); // Noncompliant@-0 {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher publisher;

                public System.Threading.Tasks.Task Publish(OrderPayload payload) => publisher.Publish(payload);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_CompliantForReadOnlyCollections() =>
        builder.AddSnippet(
            """
            namespace GP.Orders.Contracts
            {
                public class OrderLine { }

                public sealed record OrderPayload(
                    System.Collections.Generic.IReadOnlyList<OrderLine> Lines,
                    System.Collections.Generic.IReadOnlyCollection<string> Tags,
                    OrderLine[] Extras);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_CompliantForIEnumerable() =>
        builder.AddSnippet(
            """
            namespace GP.Orders.Contracts
            {
                public class OrderLine { }

                public sealed class OrderPayload
                {
                    public System.Collections.Generic.IEnumerable<OrderLine> Lines { get; init; }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_CompliantForNameSuffixAlone() =>
        builder.AddSnippet(
            """
            public class OrderLine { }

            public sealed class OrderAcceptedContract
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForFinalMessageTypeOfMassTransitStateMachinePublish() =>
        builder.AddSnippet(
            """
            namespace MassTransit
            {
                public sealed class StateMachinePublisher
                {
                    public System.Threading.Tasks.Task Publish<TSaga, TData, TMessage>(TMessage message) where TMessage : class => null;
                }
            }

            public sealed class OrderSaga { }
            public sealed class OrderData { }
            public sealed class OrderLine { }

            public sealed class OrderPayload
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}
            }

            public sealed class OrderService
            {
                private readonly MassTransit.StateMachinePublisher publisher;

                public System.Threading.Tasks.Task Publish(OrderPayload payload) =>
                    publisher.Publish<OrderSaga, OrderData, OrderPayload>(payload);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_NoncompliantForRegisteredMessageOutsideContractsNamespace() =>
        builder.AddSnippet(
            """
            namespace GP.Juno.Configuration
            {
                public sealed class AppConfig { }
            }

            namespace GP.Juno.EventStream.Api
            {
                public static class MessageRegistration
                {
                    public static void Publishes<T>(
                        this GP.Juno.Configuration.AppConfig config) where T : class { }
                }
            }

            public sealed class OrderLine { }

            public sealed class OrderPayload
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; } // Noncompliant {{'Lines' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.}}
            }

            public static class Startup
            {
                public static void Configure(GP.Juno.Configuration.AppConfig config) =>
                    GP.Juno.EventStream.Api.MessageRegistration.Publishes<OrderPayload>(config);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractCollectionsShouldBeStable_CompliantForUnrelatedPublishMethod() =>
        builder.AddSnippet(
            """
            public class OrderLine { }

            public sealed class OrderPayload
            {
                public System.Linq.IQueryable<OrderLine> Lines { get; init; }
            }

            public class OwnBus
            {
                public void Publish<T>(T value) { }
            }

            public class OrderService
            {
                public void Publish(OrderPayload payload) => new OwnBus().Publish(payload);
            }
            """)
            .VerifyNoIssues();
}
