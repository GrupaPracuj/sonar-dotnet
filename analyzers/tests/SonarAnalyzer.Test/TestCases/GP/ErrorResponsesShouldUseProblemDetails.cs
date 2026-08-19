namespace Microsoft.AspNetCore.Mvc
{
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true)]
    public sealed class ApiControllerAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class ProducesResponseTypeAttribute : System.Attribute
    {
        public ProducesResponseTypeAttribute(int statusCode) { }
        public ProducesResponseTypeAttribute(System.Type type, int statusCode) { }
    }

    public sealed class ProducesResponseTypeAttribute<T> : ProducesResponseTypeAttribute
    {
        public ProducesResponseTypeAttribute(int statusCode) : base(statusCode) { }
    }

    public interface IActionResult { }

    public class ProblemDetails { }

    public abstract class ControllerBase
    {
        protected IActionResult BadRequest() => null;
        protected IActionResult BadRequest(object error) => null;
        protected IActionResult NotFound(object value) => null;
        protected IActionResult Conflict(object error) => null;
        protected IActionResult StatusCode(int statusCode, object value) => null;
        protected IActionResult Json(object data, int statusCode) => null;
        protected IActionResult Problem(string detail = null, string title = null, int? statusCode = null) => null;
        protected IActionResult ValidationProblem() => null;
    }

    public abstract class Controller : ControllerBase { }
}

namespace Microsoft.AspNetCore.Routing
{
    public interface IEndpointRouteBuilder { }
}

namespace Microsoft.AspNetCore.Builder
{
    public static class EndpointRouteBuilderExtensions
    {
        public static void MapPost<T>(
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
        public static IResult BadRequest<T>(T error) => null;
        public static IResult NotFound<T>(T value) => null;
        public static IResult Problem(string detail = null) => null;
        public static IResult Json<T>(T data, int statusCode) => null;
    }

    public static class TypedResults
    {
        public static IResult BadRequest<T>(T error) => null;
        public static IResult Problem(Microsoft.AspNetCore.Mvc.ProblemDetails problemDetails) => null;
    }
}

public sealed class ErrorResponse { }
public sealed class CustomProblemDetails : Microsoft.AspNetCore.Mvc.ProblemDetails { }

[Microsoft.AspNetCore.Mvc.ApiController]
public sealed class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    public Microsoft.AspNetCore.Mvc.IActionResult TextError() =>
        BadRequest("Invalid order"); // Noncompliant {{Use ProblemDetails for the response body of status 400 instead of 'string'.}}

    public Microsoft.AspNetCore.Mvc.IActionResult DtoError() =>
        NotFound(new ErrorResponse()); // Noncompliant {{Use ProblemDetails for the response body of status 404 instead of 'ErrorResponse'.}}

    public Microsoft.AspNetCore.Mvc.IActionResult AnonymousError() =>
        Conflict(new { Code = "conflict" }); // Noncompliant {{Use ProblemDetails for the response body of status 409 instead of 'an anonymous type'.}}

    public Microsoft.AspNetCore.Mvc.IActionResult ExplicitStatus() =>
        StatusCode(500, new ErrorResponse()); // Noncompliant

    public Microsoft.AspNetCore.Mvc.IActionResult JsonError() =>
        Json(new ErrorResponse(), statusCode: 422); // Noncompliant

    public Microsoft.AspNetCore.Mvc.IActionResult StandardProblem() =>
        BadRequest(new Microsoft.AspNetCore.Mvc.ProblemDetails());

    public Microsoft.AspNetCore.Mvc.IActionResult DerivedProblem() =>
        NotFound(new CustomProblemDetails());

    public Microsoft.AspNetCore.Mvc.IActionResult Bodyless() =>
        BadRequest();

    public Microsoft.AspNetCore.Mvc.IActionResult ProblemFactory() =>
        Problem(title: "Invalid order", statusCode: 400);

    public Microsoft.AspNetCore.Mvc.IActionResult ValidationProblemFactory() =>
        ValidationProblem();

    [Microsoft.AspNetCore.Mvc.ProducesResponseType<ErrorResponse>(404)] // Noncompliant {{Use ProblemDetails for the response body of status 404 instead of 'ErrorResponse'.}}
    public Microsoft.AspNetCore.Mvc.IActionResult MetadataOnly() =>
        Problem(statusCode: 404);

    [Microsoft.AspNetCore.Mvc.ProducesResponseType<Microsoft.AspNetCore.Mvc.ProblemDetails>(409)]
    public Microsoft.AspNetCore.Mvc.IActionResult ProblemMetadata() =>
        Problem(statusCode: 409);

    [Microsoft.AspNetCore.Mvc.ProducesResponseType<ErrorResponse>(400)]
    public Microsoft.AspNetCore.Mvc.IActionResult MetadataAndBody() =>
        BadRequest(new ErrorResponse()); // Noncompliant

    public Microsoft.AspNetCore.Mvc.IActionResult Unreturned()
    {
        _ = BadRequest(new ErrorResponse());
        return Problem(statusCode: 400);
    }
}

[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Mvc.ProducesResponseType<ErrorResponse>(500)] // Noncompliant {{Use ProblemDetails for the response body of status 500 instead of 'ErrorResponse'.}}
public sealed class ControllerWithErrorMetadata : Microsoft.AspNetCore.Mvc.ControllerBase { }

public sealed class HomeController : Microsoft.AspNetCore.Mvc.Controller
{
    public Microsoft.AspNetCore.Mvc.IActionResult Index() =>
        BadRequest(new ErrorResponse());
}

public static class Endpoints
{
    public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
    {
        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(
            app,
            "/orders",
            () => Microsoft.AspNetCore.Http.Results.BadRequest(new ErrorResponse())); // Noncompliant

        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(
            app,
            "/orders/typed",
            () => Microsoft.AspNetCore.Http.TypedResults.BadRequest(new ErrorResponse())); // Noncompliant

        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(
            app,
            "/orders/json",
            () => Microsoft.AspNetCore.Http.Results.Json(new ErrorResponse(), statusCode: 400)); // Noncompliant

        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(
            app,
            "/orders/problem",
            () => Microsoft.AspNetCore.Http.Results.BadRequest(new CustomProblemDetails()));

        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(
            app,
            "/orders/factory",
            () => Microsoft.AspNetCore.Http.Results.Problem("Invalid order"));
    }
}
