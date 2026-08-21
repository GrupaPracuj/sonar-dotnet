/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */
#if NET

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class AcceptedResponseShouldProvideTrackingInformationTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.AcceptedResponseShouldProvideTrackingInformation>()
        .WithOptions(LanguageOptions.CSharpLatest)
        .AddReferences([
            AspNetCoreMetadataReference.MicrosoftAspNetCoreHttpAbstractions,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreHttpResults,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcAbstractions,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcCore,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcViewFeatures,
        ]);

    [TestMethod]
    public void AcceptedResponseShouldProvideTrackingInformation_MvcAccepted() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Mvc;

            public class JobsController : ControllerBase
            {
                public IActionResult Empty() => Accepted(); // Noncompliant {{Provide a tracking URI or response body with this 202 Accepted response.}}
                public IActionResult NullBody() => Accepted(value: null); // Noncompliant
                public IActionResult NullUriAndBody() => Accepted(uri: (string)null, value: null); // Noncompliant
                public IActionResult Body() => Accepted(new { operationId = 42 });
                public IActionResult Uri() => Accepted("/jobs/42");
                public IActionResult UriAndNullBody() => Accepted("/jobs/42", null);
                public IActionResult UnknownBody(object value) => Accepted(value);
                public IActionResult AtAction() => AcceptedAtAction(nameof(Status), new { id = 42 }, null);
                public IActionResult AtRoute() => AcceptedAtRoute("JobStatus", new { id = 42 }, null);
                public IActionResult Status() => Ok();
            }
            """).Verify();

    [TestMethod]
    public void AcceptedResponseShouldProvideTrackingInformation_MvcStatusCode() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            public class JobsController : ControllerBase
            {
                private const int AcceptedCode = 202;

                public IActionResult Literal() => StatusCode(202); // Noncompliant
                public IActionResult FrameworkConstant() => StatusCode(StatusCodes.Status202Accepted); // Noncompliant
                public IActionResult LocalConstant() => StatusCode(AcceptedCode); // Noncompliant
                public IActionResult NullBody() => StatusCode(statusCode: 202, value: null); // Noncompliant
                public IActionResult Body() => StatusCode(202, new { operationId = 42 });
                public IActionResult UnknownCode(int status) => StatusCode(status);
                public IActionResult Ok() => StatusCode(200);
            }
            """).Verify();

    [TestMethod]
    public void AcceptedResponseShouldProvideTrackingInformation_MinimalApi() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Http;

            public static class JobEndpoints
            {
                private const int AcceptedCode = 202;

                public static IResult EmptyAccepted() => Results.Accepted(); // Noncompliant
                public static IResult NullAccepted() => Results.Accepted(value: null); // Noncompliant
                public static IResult NullUriAndBody() => Results.Accepted(uri: (string)null, value: null); // Noncompliant
                public static IResult AcceptedBody() => Results.Accepted(value: new { operationId = 42 });
                public static IResult AcceptedUri() => Results.Accepted("/jobs/42");
                public static IResult TypedEmptyAccepted() => TypedResults.Accepted((string)null); // Noncompliant
                public static IResult TypedAcceptedUri() => TypedResults.Accepted("/jobs/42");
                public static IResult StatusLiteral() => Results.StatusCode(202); // Noncompliant
                public static IResult StatusConstant() => Results.StatusCode(AcceptedCode); // Noncompliant
                public static IResult TypedStatus() => TypedResults.StatusCode(202); // Noncompliant
                public static IResult OtherStatus() => Results.StatusCode(204);
            }
            """).Verify();

    [TestMethod]
    public void AcceptedResponseShouldProvideTrackingInformation_Lookalikes() =>
        builder.AddSnippet(
            """
            public static class Results
            {
                public static object Accepted(object value = null) => null;
                public static object StatusCode(int statusCode) => null;
            }

            public class Controller
            {
                public object Accepted() => null;
                public object StatusCode(int statusCode) => null;
                public object BuildAccepted() => Accepted();
                public object BuildStatus() => StatusCode(202);
            }

            public static class UsesLookalike
            {
                public static object BuildAccepted() => Results.Accepted();
                public static object BuildStatus() => Results.StatusCode(202);
            }
            """).VerifyNoIssues();
}

#endif
