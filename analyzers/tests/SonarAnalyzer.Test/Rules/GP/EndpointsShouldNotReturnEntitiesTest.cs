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
public class EndpointsShouldNotReturnEntitiesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.EndpointsShouldNotReturnEntities>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace System.ComponentModel.DataAnnotations
        {
            public class KeyAttribute : System.Attribute { }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public class ActionResult<T> { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok(object value) => null;
                protected IActionResult Json(object data) => null;
            }
        }

        public class Order
        {
            [System.ComponentModel.DataAnnotations.Key]
            public int Id { get; set; }
        }

        public sealed class OrderResponse
        {
            public System.Guid Id { get; set; }
        }
        """;

    private const string MinimalApiStubs =
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
                public static void MapPost<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
                public static void MapPut<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
                public static void MapPatch<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
                public static void MapDelete<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }

            public static class Results
            {
                public static IResult Ok<T>(T value) => null;
                public static IResult Json<T>(T value) => null;
            }

            public static class TypedResults
            {
                public static IResult Ok<T>(T value) => null;
                public static IResult Json<T>(T value) => null;
            }
        }
        """;

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForEntity() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Order Get(int id) => null; // Noncompliant {{'Order' is a database entity - return a response contract instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForTaskOfEntity() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Threading.Tasks.Task<Order> Get(int id) => null; // Noncompliant {{'Order' is a database entity - return a response contract instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForCollectionOfEntities() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Collections.Generic.IReadOnlyList<Order> GetAll() => null; // Noncompliant {{'Order' is a database entity - return a response contract instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForQueryable() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Linq.IQueryable<OrderResponse> GetAll() => null; // Noncompliant {{'IQueryable' is a database entity - return a response contract instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_CompliantForResponseContract() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Threading.Tasks.Task<OrderResponse> Get(int id) => null;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Collections.Generic.IReadOnlyList<OrderResponse> GetAll() => null;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForMvcResultPayload() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(Order order) =>
                    Ok(order); // Noncompliant {{'Order' is a database entity - return a response contract instead.}}

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetJson(Order order) =>
                    Json(order); // Noncompliant {{'Order' is a database entity - return a response contract instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_CompliantForMvcResultFactoryLookalike() =>
        builder.AddSnippet(
            Stubs + """

            public static class ResultFactory
            {
                public static Microsoft.AspNetCore.Mvc.IActionResult Ok(object value) => null;
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(Order order) => ResultFactory.Ok(order);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_CompliantOutsideController() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                public Order Get(int id) => null;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_CompliantForCustomKeyAttribute() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class KeyAttribute : System.Attribute { }

            public class ViewModel
            {
                [Key]
                public int Id { get; set; }
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public ViewModel Get(int id) => null;
            }
            """)
            .VerifyNoIssues();

    private const string ConfiguredEntityStubs =
        Stubs + """

        public abstract class AggregateRoot { }

        public sealed class Invoice : AggregateRoot { }

        public class InvoicesController : Microsoft.AspNetCore.Mvc.ControllerBase
        {
            [Microsoft.AspNetCore.Mvc.HttpGet]
            public Invoice Get(int id) => null; // Noncompliant {{'Invoice' is a database entity - return a response contract instead.}}
        }
        """;

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForConfiguredEntityBaseType() =>
        CreateBuilder(entityBaseTypes: "AggregateRoot")
            .AddSnippet(ConfiguredEntityStubs)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_CompliantForSameCodeWithDefaultParameters() =>
        builder.AddSnippet(ConfiguredEntityStubs.Replace(" // Noncompliant {{'Invoice' is a database entity - return a response contract instead.}}", string.Empty))
            .VerifyNoIssues();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_MinimalApiNoncompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, Order order)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/get",
                        () => Microsoft.AspNetCore.Http.Results.Ok(order)); // Noncompliant {{'Order' is a database entity - return a response contract instead.}}
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(app, "/post",
                        () => Microsoft.AspNetCore.Http.TypedResults.Ok(order)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPut(app, "/put",
                        () => Microsoft.AspNetCore.Http.Results.Json(order)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPut(app, "/typed-json",
                        () => Microsoft.AspNetCore.Http.TypedResults.Json(order)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPatch(app, "/patch",
                        () => Microsoft.AspNetCore.Http.Results.Ok(order)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/delete",
                        () => Microsoft.AspNetCore.Http.Results.Ok(order)); // Noncompliant

                    var orders = new System.Collections.Generic.List<Order>();
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/collection",
                        () => Microsoft.AspNetCore.Http.Results.Ok(orders)); // Noncompliant {{'Order' is a database entity - return a response contract instead.}}

                    System.Linq.IQueryable<OrderResponse> query = null;
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/query",
                        () => Microsoft.AspNetCore.Http.Results.Ok(query)); // Noncompliant {{'IQueryable' is a database entity - return a response contract instead.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_MinimalApiBoundariesAreCompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            namespace Custom
            {
                public static class Results
                {
                    public static Microsoft.AspNetCore.Http.IResult Ok<T>(T value) => null;
                }

                public static class Endpoints
                {
                    public static void MapGet<T>(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string pattern, System.Func<T> handler) { }
                }
            }

            public static class Endpoints
            {
                public static void Map(
                    Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app,
                    Order order,
                    OrderResponse response)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/response",
                        () => Microsoft.AspNetCore.Http.Results.Ok(response));
                    Custom.Endpoints.MapGet(app, "/map-lookalike",
                        () => Microsoft.AspNetCore.Http.Results.Ok(order));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/result-lookalike",
                        () => Custom.Results.Ok(order));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/nested", () =>
                    {
                        System.Func<Microsoft.AspNetCore.Http.IResult> nested =
                            () => Microsoft.AspNetCore.Http.Results.Ok(order);
                        return Microsoft.AspNetCore.Http.Results.Ok(response);
                    });
                }
            }
            """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilder(string entityBaseTypes = "") =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.EndpointsShouldNotReturnEntities { EntityBaseTypes = entityBaseTypes })
            .WithOptions(LanguageOptions.CSharpLatest);
}
