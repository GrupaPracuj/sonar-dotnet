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
public class DoNotRedirectToUserControlledUrlTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotRedirectToUserControlledUrl>();

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }

            public interface IUrlHelper
            {
                bool IsLocalUrl(string url);
            }

            public abstract class ControllerBase
            {
                public IUrlHelper Url { get; set; }
                protected IActionResult Redirect(string url) => null;
                protected IActionResult RedirectPermanent(string url) => null;
                protected IActionResult LocalRedirect(string localUrl) => null;
                protected IActionResult RedirectToAction(string action) => null;
                protected IActionResult BadRequest() => null;
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
                public static void MapGet<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, Microsoft.AspNetCore.Http.IResult> handler) { }
                public static void MapGet(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<Microsoft.AspNetCore.Http.IResult> handler) { }
                public static void MapPost<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, Microsoft.AspNetCore.Http.IResult> handler) { }
                public static void MapPut<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, Microsoft.AspNetCore.Http.IResult> handler) { }
                public static void MapPatch<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, Microsoft.AspNetCore.Http.IResult> handler) { }
                public static void MapDelete<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, Microsoft.AspNetCore.Http.IResult> handler) { }
                public static void MapMethods<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, string[] httpMethods, System.Func<T, Microsoft.AspNetCore.Http.IResult> handler) { }
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }

            public static class Results
            {
                public static IResult Redirect(string url, bool permanent = false, bool preserveMethod = false) => null;
            }

            public static class TypedResults
            {
                public static IResult Redirect(string url, bool permanent = false, bool preserveMethod = false) => null;
            }
        }

        """;

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_NoncompliantForActionParameter() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl) =>
                    Redirect(returnUrl); // Noncompliant {{Do not redirect to a URL taken from parameter 'returnUrl' - use LocalRedirect or check it with Url.IsLocalUrl first.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_NoncompliantForRedirectPermanent() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl) =>
                    RedirectPermanent(returnUrl); // Noncompliant {{Do not redirect to a URL taken from parameter 'returnUrl' - use LocalRedirect or check it with Url.IsLocalUrl first.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_CompliantForLocalRedirect() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl) =>
                    LocalRedirect(returnUrl);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_CompliantWhenGuardedByIsLocalUrl() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl)
                {
                    if (Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }

                    return RedirectToAction("Index");
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_NoncompliantWhenGuardedValueIsReassigned() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl, string fallbackUrl)
                {
                    if (Url.IsLocalUrl(returnUrl))
                    {
                        returnUrl = fallbackUrl;
                        return Redirect(returnUrl); // Noncompliant
                    }

                    return RedirectToAction("Index");
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_CompliantWhenNegativeGuardExitsEarly() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl)
                {
                    if (!Url.IsLocalUrl(returnUrl))
                    {
                        return BadRequest();
                    }

                    return Redirect(returnUrl);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_CompliantWhenStatementFollowsNegativeGuard() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl)
                {
                    if (!Url.IsLocalUrl(returnUrl))
                    {
                        return BadRequest();
                    }

                    System.Console.WriteLine("Redirecting to a checked local URL");
                    return Redirect(returnUrl);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_NoncompliantWhenValueIsReassignedAfterNegativeGuard() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl, string fallbackUrl)
                {
                    if (!Url.IsLocalUrl(returnUrl))
                    {
                        return BadRequest();
                    }

                    returnUrl = fallbackUrl;
                    return Redirect(returnUrl); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_NoncompliantWhenNegativeGuardDoesNotExit() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl)
                {
                    if (!Url.IsLocalUrl(returnUrl))
                    {
                        RedirectToAction("Index");
                    }

                    return Redirect(returnUrl); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_NoncompliantWhenNegativeGuardChecksDifferentUrl() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl, string fallbackUrl)
                {
                    if (!Url.IsLocalUrl(fallbackUrl))
                    {
                        return BadRequest();
                    }

                    return Redirect(returnUrl); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_CompliantForParameterInFixedPath() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Show(string id) =>
                    Redirect("/orders/" + id);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_CompliantForUrlBuiltByHelper() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string acceptanceId) =>
                    Redirect(_redirections.GetAcceptanceUrl(acceptanceId));

                private readonly Redirections _redirections = new Redirections();
            }

            public class Redirections
            {
                public string GetAcceptanceUrl(string acceptanceId) =>
                    "https://trusted.example/acceptance/" + acceptanceId;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_NoncompliantForRootPrefix() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl) =>
                    Redirect("/" + returnUrl); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_NoncompliantForUnrelatedIsLocalUrl() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl)
                {
                    if (IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl); // Noncompliant
                    }

                    return RedirectToAction("Index");
                }

                private static bool IsLocalUrl(string url) => true;
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_NoncompliantForIsLocalUrlOfDifferentArgument() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl, string fallbackUrl)
                {
                    if (Url.IsLocalUrl(fallbackUrl))
                    {
                        return Redirect(returnUrl); // Noncompliant
                    }

                    return RedirectToAction("Index");
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_CompliantForConstantUrl() =>
        builder.AddSnippet(
            Stubs + """

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl) =>
                    Redirect("/home");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_CompliantForLookalikeRedirectMethod() =>
        builder.AddSnippet(
            Stubs + """

            public static class RedirectFactory
            {
                public static Microsoft.AspNetCore.Mvc.IActionResult Redirect(string url) => null;
            }

            public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl) =>
                    RedirectFactory.Redirect(returnUrl);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("DoNotRedirectToUserControlledUrl.cs")
            .WithCodeFix<CS.DoNotRedirectToUserControlledUrlCodeFix>()
            .WithCodeFixedPaths("DoNotRedirectToUserControlledUrl.Fixed.cs")
            .VerifyCodeFix();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_MinimalApiNoncompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/get",
                        (string destination) => Microsoft.AspNetCore.Http.Results.Redirect(destination)); // Noncompliant {{Do not redirect to a URL taken from parameter 'destination' - validate that it is local or against an allowlist first.}}
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(app, "/post",
                        (string destination) => Microsoft.AspNetCore.Http.TypedResults.Redirect(destination)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPut(app, "/put",
                        (string destination) => Microsoft.AspNetCore.Http.Results.Redirect("https://" + destination)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPatch(app, "/patch",
                        (string destination) => Microsoft.AspNetCore.Http.Results.Redirect($"/{destination}")); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/delete",
                        (string destination) => Microsoft.AspNetCore.Http.Results.Redirect("/" + destination)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(app, "/methods", new[] { "GET", "POST" },
                        (string destination) => Microsoft.AspNetCore.Http.Results.Redirect(destination)); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_MinimalApiFixedLocalPathIsCompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/constant",
                        (string unused) => Microsoft.AspNetCore.Http.Results.Redirect("/home"));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(app, "/orders",
                        (string id) => Microsoft.AspNetCore.Http.Results.Redirect("/orders/" + id));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPut(app, "/interpolated-orders",
                        (string id) => Microsoft.AspNetCore.Http.TypedResults.Redirect($"/orders/{id}"));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/query",
                        (string query) => Microsoft.AspNetCore.Http.Results.Redirect("https://example.com?q=" + query));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/fragment",
                        (string fragment) => Microsoft.AspNetCore.Http.Results.Redirect($"https://example.com#{fragment}"));
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_MinimalApiCustomLocalUrlGuardIsNoncompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/positive",
                        (string destination) =>
                        {
                            if (IsLocalUrl(destination))
                            {
                                return Microsoft.AspNetCore.Http.Results.Redirect(destination); // Noncompliant
                            }

                            return Microsoft.AspNetCore.Http.Results.Redirect("/home");
                        });
                }

                private static bool IsLocalUrl(string url) => true;
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotRedirectToUserControlledUrl_MinimalApiBoundariesAreCompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            namespace Custom
            {
                public static class Results
                {
                    public static Microsoft.AspNetCore.Http.IResult Redirect(string url) => null;
                }

                public static class Endpoints
                {
                    public static void MapGet<T>(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string pattern, System.Func<T, Microsoft.AspNetCore.Http.IResult> handler) { }
                }
            }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string registrationDestination)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/nested",
                        (string destination) =>
                        {
                            System.Func<Microsoft.AspNetCore.Http.IResult> nested =
                                () => Microsoft.AspNetCore.Http.Results.Redirect(destination);
                            return nested();
                        });
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/local",
                        (string destination) =>
                        {
                            Microsoft.AspNetCore.Http.IResult RedirectFromLocal() =>
                                Microsoft.AspNetCore.Http.Results.Redirect(destination);
                            return RedirectFromLocal();
                        });
                    Custom.Endpoints.MapGet(app, "/map-lookalike",
                        (string destination) => Microsoft.AspNetCore.Http.Results.Redirect(destination));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/results-lookalike",
                        (string destination) => Custom.Results.Redirect(destination));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(
                        app,
                        Microsoft.AspNetCore.Http.Results.Redirect(registrationDestination).ToString(),
                        () => Microsoft.AspNetCore.Http.Results.Redirect("/home"));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet<string>(app, "/named", Redirect);
                }

                private static Microsoft.AspNetCore.Http.IResult Redirect(string destination) =>
                    Microsoft.AspNetCore.Http.Results.Redirect(destination);
            }
            """)
            .VerifyNoIssues();
}
