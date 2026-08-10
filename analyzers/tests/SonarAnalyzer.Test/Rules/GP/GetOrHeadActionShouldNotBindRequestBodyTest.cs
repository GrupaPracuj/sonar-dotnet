using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class GetOrHeadActionShouldNotBindRequestBodyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.GetOrHeadActionShouldNotBindRequestBody>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public class HttpHeadAttribute : System.Attribute { }
            public class HttpPostAttribute : System.Attribute { }
            public class HttpDeleteAttribute : System.Attribute { }
            public class AcceptVerbsAttribute : System.Attribute
            {
                public AcceptVerbsAttribute(params string[] methods) { }
            }
            public class FromBodyAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase { }
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
                public static void MapGet<T, TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, TResult> handler) { }
                public static void MapPost<T, TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, TResult> handler) { }
                public static void MapMethods<T, TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, string[] httpMethods, System.Func<T, TResult> handler) { }
                public static void MapGet<TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<TResult> handler) { }
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public static class HttpMethods
            {
                public static string Get => "GET";
                public static string Head => "HEAD";
            }
        }
        """;

    [TestMethod]
    public void GetOrHeadActionShouldNotBindRequestBody_NoncompliantForGetAndHead() =>
        builder.AddSnippet(
            Stubs + """

            public class SearchRequest { }

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(
                    [Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => null; // Noncompliant {{Remove '[FromBody]' from this GET action; request-body semantics are not defined for this HTTP method.}}

                [Microsoft.AspNetCore.Mvc.HttpHead]
                public Microsoft.AspNetCore.Mvc.IActionResult Head(
                    [Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => null; // Noncompliant {{Remove '[FromBody]' from this HEAD action; request-body semantics are not defined for this HTTP method.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void GetOrHeadActionShouldNotBindRequestBody_CompliantForOtherBindingsAndMethods() =>
        builder.AddSnippet(
            Stubs + """

            public class SearchRequest { }

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(SearchRequest request) => null;

                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult Post(
                    [Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => null;

                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult Delete(
                    [Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => null;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetOrHeadActionShouldNotBindRequestBody_CompliantForLookalikeAttribute() =>
        builder.AddSnippet(
            Stubs + """

            namespace Custom
            {
                public class FromBodyAttribute : System.Attribute { }
            }

            public class SearchRequest { }

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get([Custom.FromBody] SearchRequest request) => null;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetOrHeadActionShouldNotBindRequestBody_NoncompliantForAcceptVerbs() =>
        builder.AddSnippet(
            Stubs + """

            public class SearchRequest { }

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.AcceptVerbs("GET")]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(
                    [Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => null; // Noncompliant {{Remove '[FromBody]' from this GET action; request-body semantics are not defined for this HTTP method.}}

                [Microsoft.AspNetCore.Mvc.AcceptVerbs("POST", "HEAD")]
                public Microsoft.AspNetCore.Mvc.IActionResult Head(
                    [Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => null; // Noncompliant {{Remove '[FromBody]' from this HEAD action; request-body semantics are not defined for this HTTP method.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void GetOrHeadActionShouldNotBindRequestBody_CompliantForAcceptVerbsLookalike() =>
        builder.AddSnippet(
            Stubs + """

            namespace Custom
            {
                public class AcceptVerbsAttribute : System.Attribute
                {
                    public AcceptVerbsAttribute(params string[] methods) { }
                }
            }

            public class SearchRequest { }

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Custom.AcceptVerbs("GET")]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(
                    [Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => null;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetOrHeadActionShouldNotBindRequestBody_MinimalApiNoncompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            public class SearchRequest { }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/get",
                        ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok"); // Noncompliant {{Remove '[FromBody]' from this GET action; request-body semantics are not defined for this HTTP method.}}
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(app, "/head", new[] { "POST", "HEAD" },
                        ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok"); // Noncompliant {{Remove '[FromBody]' from this HEAD action; request-body semantics are not defined for this HTTP method.}}
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(app, "/get-property",
                        new[] { Microsoft.AspNetCore.Http.HttpMethods.Get },
                        ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok"); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetOrHeadActionShouldNotBindRequestBody_MinimalApiBoundariesAreCompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            namespace Custom
            {
                public class FromBodyAttribute : System.Attribute { }

                public static class Endpoints
                {
                    public static void MapGet<T, TResult>(
                        Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app,
                        string pattern,
                        System.Func<T, TResult> handler) { }
                }
            }

            public class SearchRequest { }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(app, "/post",
                        ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok");
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/attribute-lookalike",
                        ([Custom.FromBody] SearchRequest request) => "ok");
                    Custom.Endpoints.MapGet(app, "/map-lookalike",
                        ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok");
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(app, "/patch", new[] { "PATCH" },
                        ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok");
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(app, "/conditional",
                        new[] { false ? "GET" : "POST" },
                        ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok");
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(app, "/dead-constant",
                        new[] { true ? "GET" : "POST" },
                        ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok");
                    var methods = new[] { "GET" };
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(app, "/indirect", methods,
                        ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok");
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/nested", () =>
                    {
                        System.Func<SearchRequest, string> nested =
                            ([Microsoft.AspNetCore.Mvc.FromBody] SearchRequest request) => "ok";
                        return "ok";
                    });
                }
            }
            """)
            .VerifyNoIssues();
}
