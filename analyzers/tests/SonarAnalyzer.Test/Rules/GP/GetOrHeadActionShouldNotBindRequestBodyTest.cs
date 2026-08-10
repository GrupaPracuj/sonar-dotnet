using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class GetOrHeadActionShouldNotBindRequestBodyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.GetOrHeadActionShouldNotBindRequestBody>();

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
}
