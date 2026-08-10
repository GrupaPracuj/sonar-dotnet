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
    public void DoNotRedirectToUserControlledUrl_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("DoNotRedirectToUserControlledUrl.cs")
            .WithCodeFix<CS.DoNotRedirectToUserControlledUrlCodeFix>()
            .WithCodeFixedPaths("DoNotRedirectToUserControlledUrl.Fixed.cs")
            .VerifyCodeFix();
}
