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
public class EndpointsShouldNotExposeExceptionDetailsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.EndpointsShouldNotExposeExceptionDetails>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok(object value) => null;
                protected IActionResult StatusCode(int code, object value) => null;
                protected IActionResult Problem(string title, int statusCode) => null;
                protected IActionResult Unauthorized(object value) => null;
                protected IActionResult Created(string uri, object value) => null;
                protected IActionResult CreatedAtAction(string action, object value) => null;
                protected IActionResult CreatedAtRoute(string route, object value) => null;
                protected IActionResult Accepted(object value) => null;
                protected IActionResult AcceptedAtAction(string action, object value) => null;
                protected IActionResult AcceptedAtRoute(string route, object value) => null;
            }
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
                public static void MapMethods<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, string[] httpMethods, System.Func<T> handler) { }
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
                public static IResult BadRequest<T>(T value) => null;
            }
        }
        """;

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_NoncompliantForMessageInResponse() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    try
                    {
                        return Ok("order");
                    }
                    catch (System.Exception ex)
                    {
                        return StatusCode(500, ex.Message); // Noncompliant {{Do not put 'Exception.Message' in a response - return a ProblemDetails without internal details.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_NoncompliantForAdditionalMvcBodyFactories() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult UnauthorizedResponse(System.Exception ex) =>
                    Unauthorized(ex.Message); // Noncompliant

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult CreatedResponse(System.Exception ex) =>
                    Created("/orders/1", ex.Message); // Noncompliant

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult CreatedAtActionResponse(System.Exception ex) =>
                    CreatedAtAction("Get", ex.StackTrace); // Noncompliant

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult CreatedAtRouteResponse(System.Exception ex) =>
                    CreatedAtRoute("orders", ex.Source); // Noncompliant

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult AcceptedResponse(System.Exception ex) =>
                    Accepted(ex.InnerException); // Noncompliant

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult AcceptedAtActionResponse(System.Exception ex) =>
                    AcceptedAtAction("Get", ex.ToString()); // Noncompliant

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult AcceptedAtRouteResponse(System.Exception ex) =>
                    AcceptedAtRoute("orders", ex.Message); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_NoncompliantForWrappedMessage() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(System.Exception ex) =>
                    Ok(new { Error = $"Request failed: {ex.Message}" }); // Noncompliant {{Do not put 'Exception.Message' in a response - return a ProblemDetails without internal details.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_ReportsOnlyOutermostExceptionDetail() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(System.Exception ex) =>
                    Ok(ex.InnerException.Message); // Noncompliant {{Do not put 'Exception.Message' in a response - return a ProblemDetails without internal details.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_NoncompliantForStackTraceReturned() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public string Get()
                {
                    try
                    {
                        return "order";
                    }
                    catch (System.InvalidOperationException ex)
                    {
                        return ex.StackTrace; // Noncompliant {{Do not put 'InvalidOperationException.StackTrace' in a response - return a ProblemDetails without internal details.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_NoncompliantForToString() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    try
                    {
                        return Ok("order");
                    }
                    catch (System.Exception ex)
                    {
                        return Ok(ex.ToString()); // Noncompliant {{Do not put 'Exception.ToString' in a response - return a ProblemDetails without internal details.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_CompliantForProblemDetails() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    try
                    {
                        return Ok("order");
                    }
                    catch (System.Exception)
                    {
                        return Problem("The order could not be read.", 500);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_CompliantForDerivedValueAndLookalikeFactory() =>
        builder.AddSnippet(
            Stubs + """

            public static class CustomResults
            {
                public static Microsoft.AspNetCore.Mvc.IActionResult Ok(object value) => null;
                public static Microsoft.AspNetCore.Mvc.IActionResult Unauthorized(object value) => null;
                public static Microsoft.AspNetCore.Mvc.IActionResult Created(string uri, object value) => null;
                public static Microsoft.AspNetCore.Mvc.IActionResult Accepted(object value) => null;
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Length(System.Exception ex) =>
                    Ok(ex.Message.Length);

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Lookalike(System.Exception ex) =>
                    CustomResults.Ok(ex.Message);

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult AdditionalDerivedValue(System.Exception ex) =>
                    Unauthorized(ex.Message.Length);

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult CreatedDerivedValue(System.Exception ex) =>
                    Created("/orders/1", ex.StackTrace.Length);

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult AcceptedDerivedValue(System.Exception ex) =>
                    Accepted(ex.Source.Length);

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LookalikeUnauthorized(System.Exception ex) =>
                    CustomResults.Unauthorized(ex.Message);

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LookalikeCreated(System.Exception ex) =>
                    CustomResults.Created("/orders/1", ex.StackTrace);

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LookalikeAccepted(System.Exception ex) =>
                    CustomResults.Accepted(ex.Source);
            }
            """)
            .VerifyNoIssues();

    // The log is where exception detail belongs, so logging it is not reported.
    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_CompliantForLogging() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    try
                    {
                        return Ok("order");
                    }
                    catch (System.Exception ex)
                    {
                        System.Console.WriteLine(ex.Message);
                        return Problem("The order could not be read.", 500);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_CompliantOutsideController() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                public string Describe()
                {
                    try
                    {
                        return "order";
                    }
                    catch (System.Exception ex)
                    {
                        return ex.Message;
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_MinimalApiNoncompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, System.Exception exception)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/message",
                        () => Microsoft.AspNetCore.Http.Results.Ok(exception.Message)); // Noncompliant {{Do not put 'Exception.Message' in a response - return a ProblemDetails without internal details.}}
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(app, "/stack",
                        () => Microsoft.AspNetCore.Http.TypedResults.BadRequest(exception.StackTrace)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(app, "/string", new[] { "PATCH" },
                        () => Microsoft.AspNetCore.Http.Results.Json(exception.ToString())); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/raw",
                        () => exception.Source); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/wrapped",
                        () => Microsoft.AspNetCore.Http.Results.Ok(new { Error = $"Failed: {exception.Message}" })); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_MinimalApiBoundariesAreCompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            namespace Custom
            {
                public static class Results
                {
                    public static Microsoft.AspNetCore.Http.IResult Ok<T>(T value) => null;
                }
            }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, System.Exception exception)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/length",
                        () => Microsoft.AspNetCore.Http.Results.Ok(exception.Message.Length));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/bool",
                        () => Microsoft.AspNetCore.Http.Results.Ok(exception is System.InvalidOperationException));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/alias", () =>
                    {
                        var message = exception.Message;
                        return Microsoft.AspNetCore.Http.Results.Ok(message);
                    });
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/logging", () =>
                    {
                        System.Console.WriteLine(exception.StackTrace);
                        return Microsoft.AspNetCore.Http.Results.Ok("failed");
                    });
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/lookalike",
                        () => Custom.Results.Ok(exception.Message));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/nested", () =>
                    {
                        System.Func<Microsoft.AspNetCore.Http.IResult> nested =
                            () => Microsoft.AspNetCore.Http.Results.Ok(exception.Message);
                        return Microsoft.AspNetCore.Http.Results.Ok("failed");
                    });
                }
            }
            """)
            .VerifyNoIssues();
}
