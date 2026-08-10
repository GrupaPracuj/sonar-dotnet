using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DeleteEndpointsShouldNotReturnContentTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DeleteEndpointsShouldNotReturnContent>();

    private const string ControllerStubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpDeleteAttribute : System.Attribute { }
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
                protected IActionResult Ok(object value) => null;
                protected IActionResult NoContent() => null;
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
                public static void MapDelete<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
                public static void MapGet<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }

            public static class Results
            {
                public static IResult Ok() => null;
                public static IResult Ok<T>(T value) => null;
                public static IResult Json<T>(T value, int? statusCode = null) => null;
                public static IResult Text(string value, int? statusCode = null) => null;
                public static IResult Content(string value, int? statusCode = null) => null;
                public static IResult BadRequest<T>(T value) => null;
                public static IResult NoContent() => null;
            }

            public static class TypedResults
            {
                public static IResult Ok<T>(T value) => null;
                public static IResult Json<T>(T value, int? statusCode = null) => null;
                public static IResult Text(string value, int? statusCode = null) => null;
                public static IResult Content(string value, int? statusCode = null) => null;
                public static IResult NoContent() => null;
            }
        }
        """;

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_NoncompliantForOkWithBody() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteOrder(int id)
                {
                    var deleted = new { Id = id };
                    return Ok(deleted); // Noncompliant {{DELETE endpoints should return 204 (NoContent) instead of 200 with a response body.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_CompliantForNoContent() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteOrder(int id) => NoContent();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_CompliantForOkWithoutBody() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteOrder(int id) => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_CompliantForNonDeleteMethod() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetOrder(int id) => Ok(new { Id = id });
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_MinimalApi() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/1",
                        () => Microsoft.AspNetCore.Http.Results.Ok(new object())); // Noncompliant

                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/2",
                        () => Microsoft.AspNetCore.Http.TypedResults.Ok(new object())); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/3",
                        () => Microsoft.AspNetCore.Http.Results.Json(new object())); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/4",
                        () => Microsoft.AspNetCore.Http.TypedResults.Json(new object(), statusCode: 200)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/5",
                        () => Microsoft.AspNetCore.Http.Results.Text("deleted")); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/6",
                        () => Microsoft.AspNetCore.Http.TypedResults.Content("deleted")); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_MinimalApiOtherStatusAndBodyShapesAreCompliant() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, int statusCode)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/1",
                        () => Microsoft.AspNetCore.Http.Results.Json(new object(), statusCode: 202));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/2",
                        () => Microsoft.AspNetCore.Http.TypedResults.Text("deleted", statusCode: 201));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/3",
                        () => Microsoft.AspNetCore.Http.Results.Content("deleted", statusCode: statusCode));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/4",
                        () => Microsoft.AspNetCore.Http.Results.BadRequest(new object()));
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_MinimalApiBoundariesAreCompliant() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            namespace Custom
            {
                public static class Endpoints
                {
                    public static void MapDelete<T>(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string pattern, System.Func<T> handler) { }
                }
            }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/1",
                        () => Microsoft.AspNetCore.Http.Results.Ok());
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/orders/2",
                        () => Microsoft.AspNetCore.Http.Results.Ok(new object()));
                    Custom.Endpoints.MapDelete(app, "/orders/3",
                        () => Microsoft.AspNetCore.Http.Results.Ok(new object()));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/orders/4", () =>
                    {
                        System.Func<Microsoft.AspNetCore.Http.IResult> nested =
                            () => Microsoft.AspNetCore.Http.Results.Ok(new object());
                        Microsoft.AspNetCore.Http.IResult Local() =>
                            Microsoft.AspNetCore.Http.Results.Ok(new object());
                        return Microsoft.AspNetCore.Http.Results.NoContent();
                    });
                }
            }
            """)
            .VerifyNoIssues();

    // An MVC action may return an IResult, so the Minimal API factory counts as a response factory there too.
    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_NoncompliantForMinimalApiFactoryInsideAnAction() =>
        builder.AddSnippet(
            ControllerStubs + MinimalApiStubs + """

            public class FilesController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Http.IResult Delete(int id)
                {
                    return Microsoft.AspNetCore.Http.Results.Ok(id); // Noncompliant {{DELETE endpoints should return 204 (NoContent) instead of 200 with a response body.}}
                }
            }
            """)
            .Verify();

    // "Ok" is resolved to ControllerBase: a same-named helper on the controller itself is not the MVC 200 factory.
    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_CompliantForLookalikeOk() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class FilesController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private static object Ok(object value, bool acknowledged) => null;

                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public object Delete(int id) => Ok(id, true);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("DeleteEndpointsShouldNotReturnContent.cs")
            .WithCodeFix<CS.DeleteEndpointsShouldNotReturnContentCodeFix>()
            .WithCodeFixedPaths("DeleteEndpointsShouldNotReturnContent.Fixed.cs")
            .VerifyCodeFix();
}
