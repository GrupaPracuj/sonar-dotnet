using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotInterpolateRequestInputIntoLogMessageTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotInterpolateRequestInputIntoLogMessage>();

    private const string Stubs =
        """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }

            public static class LoggerExtensions
            {
                public static void LogInformation(this ILogger logger, string message, params object[] args) { }
            }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
            }
        }
        """;

    [TestMethod]
    public void DoNotInterpolateRequestInputIntoLogMessage_NoncompliantForInterpolatedActionParameter() =>
        builder.AddSnippet(
            Stubs + """

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Search(string query)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, $"Searching for {query}"); // Noncompliant {{Pass 'query' as a logging argument instead of interpolating it into the message template - it comes straight from the request.}}
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotInterpolateRequestInputIntoLogMessage_NoncompliantForConcatenatedActionParameter() =>
        builder.AddSnippet(
            Stubs + """

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Search(string query)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Searching for " + query); // Noncompliant {{Pass 'query' as a logging argument instead of interpolating it into the message template - it comes straight from the request.}}
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotInterpolateRequestInputIntoLogMessage_CompliantForStructuredTemplate() =>
        builder.AddSnippet(
            Stubs + """

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Search(string query)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Searching for {Query}", query);
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotInterpolateRequestInputIntoLogMessage_CompliantForExpressionStructuredArgument() =>
        builder.AddSnippet(
            Stubs + """

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Sum(int left, int right)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Sum: {Sum}", left + right);
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotInterpolateRequestInputIntoLogMessage_CompliantForInterpolatedNonRequestValue() =>
        builder.AddSnippet(
            Stubs + """

            public class SearchController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;
                private readonly string _instance = "search-1";

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Search(string query)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, $"Serving from {_instance}");
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotInterpolateRequestInputIntoLogMessage_CompliantOutsideController() =>
        builder.AddSnippet(
            Stubs + """

            public class SearchService
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Search(string query) =>
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, $"Searching for {query}");
            }
            """)
            .VerifyNoIssues();
}
