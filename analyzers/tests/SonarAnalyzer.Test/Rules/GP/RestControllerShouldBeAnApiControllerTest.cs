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
public class RestControllerShouldBeAnApiControllerTest
{
    private const string MvcStub =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class RouteAttribute : System.Attribute { public RouteAttribute(string template) { } }
            public class HttpGetAttribute : System.Attribute { }
            public class ApiControllerAttribute : System.Attribute { }
            public class ProducesAttribute : System.Attribute { public ProducesAttribute(string contentType) { } }
            public class ProducesResponseTypeAttribute : System.Attribute
            {
                public ProducesResponseTypeAttribute(int statusCode) { }
                public ProducesResponseTypeAttribute(System.Type type, int statusCode) { }
            }
            public interface IActionResult { }
            public class ViewResult : IActionResult { }
            public class PartialViewResult : IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok(object value) => null;
                protected IActionResult NoContent() => null;
            }
            public abstract class Controller : ControllerBase
            {
                public dynamic ViewBag => null;
                protected ViewResult View(object model) => null;
                protected PartialViewResult PartialView(object model) => null;
            }
        }

        public sealed class UserFiles { }
        """;

    [TestMethod]
    public void RestControllerShouldBeAnApiController_NoncompliantForDeclaredResponseType() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                [Microsoft.AspNetCore.Mvc.Route("files")]
                public class FilesController : Microsoft.AspNetCore.Mvc.Controller // Noncompliant {{Derive 'FilesController' from ControllerBase or mark it [ApiController]; it serves REST but is declared as a view-rendering controller.}}
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(UserFiles), 200)]
                    public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(new UserFiles());
                }
                """)
            .Verify();

    [TestMethod]
    public void RestControllerShouldBeAnApiController_NoncompliantForProducedMediaType() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                [Microsoft.AspNetCore.Mvc.Produces("application/json")]
                public class MaintenanceController : Microsoft.AspNetCore.Mvc.Controller // Noncompliant
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(new UserFiles());
                }
                """)
            .Verify();

    [TestMethod]
    public void RestControllerShouldBeAnApiController_CompliantForApiController() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                [Microsoft.AspNetCore.Mvc.ApiController]
                [Microsoft.AspNetCore.Mvc.Route("files")]
                public class FilesController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(UserFiles), 200)]
                    public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(new UserFiles());
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void RestControllerShouldBeAnApiController_CompliantForApiControllerAttributeOnViewRenderingBase() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                [Microsoft.AspNetCore.Mvc.ApiController]
                public class FilesController : Microsoft.AspNetCore.Mvc.Controller
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(UserFiles), 200)]
                    public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(new UserFiles());
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void RestControllerShouldBeAnApiController_CompliantForRenderedView() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public class FilesController : Microsoft.AspNetCore.Mvc.Controller
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(UserFiles), 200)]
                    public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(new UserFiles());

                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public Microsoft.AspNetCore.Mvc.IActionResult Index() => View(new UserFiles());
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void RestControllerShouldBeAnApiController_CompliantForViewResultReturnType() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public class FilesController : Microsoft.AspNetCore.Mvc.Controller
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(UserFiles), 200)]
                    public Microsoft.AspNetCore.Mvc.ViewResult Get() => null;
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void RestControllerShouldBeAnApiController_CompliantForViewState() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public class FilesController : Microsoft.AspNetCore.Mvc.Controller
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(UserFiles), 200)]
                    public Microsoft.AspNetCore.Mvc.IActionResult Get()
                    {
                        ViewBag.Files = new UserFiles();
                        return Ok(null);
                    }
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void RestControllerShouldBeAnApiController_CompliantWithoutRestEvidence() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public class QueueActionsController : Microsoft.AspNetCore.Mvc.Controller
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public Microsoft.AspNetCore.Mvc.IActionResult Execute() => Ok(new UserFiles());
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void RestControllerShouldBeAnApiController_CompliantForStatusOnlyResponseMetadata() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public class QueueActionsController : Microsoft.AspNetCore.Mvc.Controller
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    [Microsoft.AspNetCore.Mvc.ProducesResponseType(204)]
                    public Microsoft.AspNetCore.Mvc.IActionResult Execute() => NoContent();
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void RestControllerShouldBeAnApiController_CompliantForAbstractBase() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                [Microsoft.AspNetCore.Mvc.Produces("application/json")]
                public abstract class ApiBaseController : Microsoft.AspNetCore.Mvc.Controller
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(null);
                }
                """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilder() =>
        new VerifierBuilder<CS.RestControllerShouldBeAnApiController>().WithOptions(LanguageOptions.CSharpLatest);
}
