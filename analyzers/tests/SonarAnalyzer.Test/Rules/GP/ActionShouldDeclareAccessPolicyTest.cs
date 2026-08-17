using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ActionShouldDeclareAccessPolicyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ActionShouldDeclareAccessPolicy>();

    private const string ControllerStubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public class AuthorizeAttribute : System.Attribute { }
            public class AllowAnonymousAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
            }
        }
        """;

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantForMissingDeclaration() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() // Noncompliant {{Method 'GetSettings' has neither [Authorize] nor [AllowAnonymous], but other actions in 'UsersController' are explicitly protected with [Authorize].}}
                {
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenExplicitlyAnonymous() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.AllowAnonymous]
                public Microsoft.AspNetCore.Mvc.IActionResult GetStatus() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenNoActionUsesAuthorize() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class PublicController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetHealth() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetVersion() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenClassLevelAuthorize() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Mvc.Authorize]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() => Ok();
            }
            """)
            .VerifyNoIssues();

    // ASP.NET Core honours [Authorize] inherited from a shared base controller, so the derived controller has nothing
    // left to declare.
    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenBaseControllerDeclaresAuthorize() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Mvc.Authorize]
            public abstract class SecuredControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase { }

            public class UsersController : SecuredControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenBaseControllerDeclaresAllowAnonymous() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Mvc.AllowAnonymous]
            public abstract class PublicControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase { }

            public class UsersController : PublicControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetStatus() => Ok();
            }
            """)
            .VerifyNoIssues();

    // A base class that declares no policy changes nothing: the per-action convention still applies.
    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantWhenBaseControllerDeclaresNothing() =>
        builder.AddSnippet(
            ControllerStubs + """

            public abstract class PlainControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase { }

            public class UsersController : PlainControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() => Ok(); // Noncompliant {{Method 'GetSettings' has neither [Authorize] nor [AllowAnonymous], but other actions in 'UsersController' are explicitly protected with [Authorize].}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_ReportsOnceForSameFilePartialController() =>
        builder.AddSnippet(
            ControllerStubs + """

            public partial class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();
            }

            public partial class UsersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() => Ok(); // Noncompliant
            }
            """)
            .Verify();
}
