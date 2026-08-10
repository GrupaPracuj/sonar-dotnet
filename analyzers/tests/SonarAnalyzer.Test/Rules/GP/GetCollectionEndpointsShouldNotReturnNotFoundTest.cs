using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class GetCollectionEndpointsShouldNotReturnNotFoundTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.GetCollectionEndpointsShouldNotReturnNotFound>()
        .AddReferences(MetadataReferenceFacade.SystemThreadingTasks);

    private const string MinimalApiStubs =
        """
        global using Microsoft.AspNetCore.Builder;
        global using System.Linq;

        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static Microsoft.AspNetCore.Routing.IEndpointRouteBuilder MapGroup(
                    this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints,
                    string prefix) => endpoints;

                public static void MapGet<T>(
                    this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints,
                    string pattern,
                    System.Func<T> handler) { }
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }

            public static class Results
            {
                public static IResult NotFound() => null;
                public static IResult StatusCode(int statusCode) => null;
                public static IResult Ok<T>(T value) => null;
            }

            public static class TypedResults
            {
                public static IResult NotFound() => null;
                public static IResult Ok<T>(T value) => null;
            }
        }
        """;

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForNotFound() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NotFound<T>() => null;
                    protected ActionResult<T> StatusCode<T>(int code) => null;
                    protected ActionResult<T> Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<string>> GetUsers()
                {
                    return NotFound<System.Collections.Generic.IReadOnlyList<string>>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForStatusCode404() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NotFound<T>() => null;
                    protected ActionResult<T> StatusCode<T>(int code) => null;
                    protected ActionResult<T> Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IEnumerable<string>> GetUsers()
                {
                    return StatusCode<System.Collections.Generic.IEnumerable<string>>(404); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForOkEmptyCollection() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<string>> GetUsers()
                {
                    return Ok<System.Collections.Generic.IReadOnlyList<string>>(new List<string>());
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForNonGetMethod() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NotFound<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<string>> DeleteUsers()
                {
                    return NotFound<System.Collections.Generic.IReadOnlyList<string>>();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForArrayReturnType() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NotFound<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<string[]> GetUsers()
                {
                    return NotFound<string[]>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForConcreteListReturnType() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NotFound<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<List<string>> GetUsers()
                {
                    return NotFound<List<string>>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForValueTaskWrappedActionResult() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NotFound<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async ValueTask<Microsoft.AspNetCore.Mvc.ActionResult<IEnumerable<string>>> GetUsersAsync()
                {
                    await Task.Yield();
                    return NotFound<IEnumerable<string>>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .WithOptions(LanguageOptions.FromCSharp8)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForPlainIActionResult() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetUsers(bool empty)
                {
                    if (empty)
                    {
                        return NotFound(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                    }

                    return Ok(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForPlainIActionResultWithExplicitGenericOk() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetUsers(bool empty)
                {
                    if (empty)
                    {
                        return NotFound(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                    }

                    return Ok<List<string>>(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForTaskWrappedIActionResult() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async Task<Microsoft.AspNetCore.Mvc.IActionResult> GetUsersAsync(bool empty)
                {
                    await Task.Yield();
                    if (empty)
                    {
                        return NotFound(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                    }

                    return Ok(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForPlainIActionResultReturningSingleObject() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class User { }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetUser(bool missing)
                {
                    if (missing)
                    {
                        return NotFound();
                    }

                    return Ok(new User());
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForMinimalApiResults() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool empty) =>
                    app.MapGet("/users", () =>
                    {
                        if (empty)
                        {
                            return Microsoft.AspNetCore.Http.Results.NotFound(); // Noncompliant
                        }

                        return Microsoft.AspNetCore.Http.Results.Ok(new System.Collections.Generic.List<string>());
                    });
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForMinimalApiTypedResults() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool empty) =>
                    app.MapGet("/users", () => empty
                        ? Microsoft.AspNetCore.Http.TypedResults.NotFound() // Noncompliant
                        : Microsoft.AspNetCore.Http.TypedResults.Ok(new string[0]));
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForMinimalApiStatusCode404() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool empty) =>
                    app.MapGet("/users", () => empty
                        ? Microsoft.AspNetCore.Http.Results.StatusCode(404) // Noncompliant
                        : Microsoft.AspNetCore.Http.Results.Ok(new string[0]));
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForMinimalApiReturningSingleObject() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public sealed class User { }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool missing) =>
                    app.MapGet("/users/{id}", () => missing
                        ? Microsoft.AspNetCore.Http.Results.NotFound()
                        : Microsoft.AspNetCore.Http.Results.Ok(new User()));
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForLookalikeMapGet() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public sealed class CustomApp
            {
                public void MapGet<T>(string pattern, System.Func<T> handler) { }
            }

            public static class Endpoints
            {
                public static void Map(CustomApp app, bool empty) =>
                    app.MapGet("/users", () => empty
                        ? Microsoft.AspNetCore.Http.Results.NotFound()
                        : Microsoft.AspNetCore.Http.Results.Ok(new string[0]));
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForMissingParentResource() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute
                {
                    public HttpGetAttribute(string template) { }
                }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("customers/{customerId}/orders")]
                public Microsoft.AspNetCore.Mvc.IActionResult GetOrders(int customerId)
                {
                    if (!CustomerExists(customerId))
                    {
                        return NotFound();
                    }

                    return Ok(new List<string>());
                }

                private static bool CustomerExists(int customerId) => false;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForEmptyCollectionUnderParent() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute
                {
                    public HttpGetAttribute(string template) { }
                }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("customers/{customerId}/orders")]
                public Microsoft.AspNetCore.Mvc.IActionResult GetOrders(int customerId)
                {
                    var orders = new List<string>();
                    if (orders.Count == 0)
                    {
                        return NotFound(); // Noncompliant
                    }

                    return Ok(orders);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantWhenDifferentCollectionIsEmpty() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute
                {
                    public HttpGetAttribute(string template) { }
                }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("customers/{customerId}/orders")]
                public Microsoft.AspNetCore.Mvc.IActionResult GetOrders()
                {
                    var cache = new List<string>();
                    var orders = new List<string>();
                    if (cache.Count == 0)
                    {
                        return NotFound();
                    }

                    return Ok(orders);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantWhenAbsoluteActionRouteRemovesParent() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class RouteAttribute : System.Attribute
                {
                    public RouteAttribute(string template) { }
                }
                public class HttpGetAttribute : System.Attribute
                {
                    public HttpGetAttribute(string template) { }
                }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            [Microsoft.AspNetCore.Mvc.Route("customers/{customerId}")]
            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("/orders")]
                public Microsoft.AspNetCore.Mvc.IActionResult GetOrders()
                {
                    return NotFound(); // Noncompliant
                    return Ok(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantWhenAnyActionRouteShowsParent() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class RouteAttribute : System.Attribute
                {
                    public RouteAttribute(string template) { }
                }
                public class HttpGetAttribute : System.Attribute
                {
                    public HttpGetAttribute(string template) { }
                }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("customers/{customerId}/orders")]
                [Microsoft.AspNetCore.Mvc.Route("/legacy-orders")]
                public Microsoft.AspNetCore.Mvc.IActionResult GetOrders(int customerId)
                {
                    if (!CustomerExists(customerId))
                    {
                        return NotFound();
                    }

                    return Ok(new List<string>());
                }

                private static bool CustomerExists(int customerId) => false;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForMissingParentInMinimalApi() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool customerExists) =>
                    app.MapGet("/customers/{customerId}/orders", () =>
                    {
                        if (!customerExists)
                        {
                            return Microsoft.AspNetCore.Http.Results.NotFound();
                        }

                        return Microsoft.AspNetCore.Http.Results.Ok(new System.Collections.Generic.List<string>());
                    });
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForParentInMinimalApiRouteGroup() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool customerExists) =>
                    app.MapGroup("/customers/{customerId}").MapGet("/orders", () => customerExists
                        ? Microsoft.AspNetCore.Http.Results.Ok(new string[0])
                        : Microsoft.AspNetCore.Http.Results.NotFound());
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForEmptyCollectionUnderParentInMinimalApi() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
                    app.MapGet("/customers/{customerId}/orders", () =>
                    {
                        var orders = new System.Collections.Generic.List<string>();
                        if (!orders.Any())
                        {
                            return Microsoft.AspNetCore.Http.Results.NotFound(); // Noncompliant
                        }

                        return Microsoft.AspNetCore.Http.Results.Ok(orders);
                    });
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForInverseEmptyCollectionCondition() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
                    app.MapGet("/customers/{customerId}/orders", () =>
                    {
                        var orders = new System.Collections.Generic.List<string>();
                        return orders.Any()
                            ? Microsoft.AspNetCore.Http.Results.Ok(orders)
                            : Microsoft.AspNetCore.Http.Results.NotFound(); // Noncompliant
                    });
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantWhenOuterConditionChecksUnrelatedCollection() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(
                    Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app,
                    System.Collections.Generic.List<string> enabledFeatures,
                    bool customerExists)
                {
                    if (enabledFeatures.Count == 0)
                    {
                        app.MapGet("/customers/{customerId}/orders", () => customerExists
                            ? Microsoft.AspNetCore.Http.Results.Ok(new string[0])
                            : Microsoft.AspNetCore.Http.Results.NotFound());
                    }
                }
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .VerifyNoIssues();

    // "NotFound" is resolved to ControllerBase: a same-named helper on the controller itself is not the MVC 404 factory.
    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForLookalikeNotFound() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private static IEnumerable<string> NotFound() => new string[0];

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public IEnumerable<string> GetUsers() => NotFound();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("GetCollectionEndpointsShouldNotReturnNotFound.cs")
            .WithCodeFix<CS.GetCollectionEndpointsShouldNotReturnNotFoundCodeFix>()
            .WithCodeFixedPaths("GetCollectionEndpointsShouldNotReturnNotFound.Fixed.cs")
            .VerifyCodeFix();
}
