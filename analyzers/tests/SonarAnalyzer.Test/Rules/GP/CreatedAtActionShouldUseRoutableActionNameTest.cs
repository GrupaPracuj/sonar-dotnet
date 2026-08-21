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
public class CreatedAtActionShouldUseRoutableActionNameTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CreatedAtActionShouldUseRoutableActionName>()
        .WithOptions(LanguageOptions.CSharpLatest)
        .AddReferences([
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcAbstractions,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcCore,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcViewFeatures,
        ]);

    [TestMethod]
    public void CreatedAtActionShouldUseRoutableActionName_DefaultOption() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Mvc;

            public class OrdersController : ControllerBase
            {
                public IActionResult Create() =>
                    CreatedAtAction(nameof(GetAsync), new { id = 1 }, new object()); // Noncompliant {{This Async-suffixed action name is suppressed by MVC; use a named route with CreatedAtRoute instead.}}

                public IActionResult CreateNamed() =>
                    CreatedAtAction(value: new object(), routeValues: new { id = 1 }, actionName: nameof(GetAsync)); // Noncompliant

                public IActionResult CreateShortOverload() =>
                    CreatedAtAction(nameof(GetAsync), new object()); // Noncompliant

                public IActionResult CreateSync() =>
                    CreatedAtAction(nameof(Get), new { id = 1 }, new object());

                public IActionResult Get() => Ok();
                public IActionResult GetAsync() => Ok();
            }
            """).Verify();

    [TestMethod]
    public void CreatedAtActionShouldUseRoutableActionName_CrossControllerAndActionName() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Mvc;

            public class OrdersController : ControllerBase
            {
                public IActionResult Create() =>
                    CreatedAtAction(nameof(QueriesController.GetAsync), "Queries", new { id = 1 }, new object()); // Noncompliant

                public IActionResult CreateExplicitActionName() =>
                    CreatedAtAction(nameof(GetExplicitAsync), new { id = 1 }, new object());

                [ActionName("GetExplicitAsync")]
                public IActionResult GetExplicitAsync() => Ok();
            }

            public class QueriesController : ControllerBase
            {
                public IActionResult GetAsync() => Ok();
            }
            """).Verify();

    [TestMethod]
    public void CreatedAtActionShouldUseRoutableActionName_OptionFalse() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Mvc;

            public static class MvcConfiguration
            {
                public static void Configure(MvcOptions options) =>
                    options.SuppressAsyncSuffixInActionNames = false;
            }

            public class OrdersController : ControllerBase
            {
                public IActionResult Create() =>
                    CreatedAtAction(nameof(GetAsync), new { id = 1 }, new object());

                public IActionResult GetAsync() => Ok();
            }
            """).VerifyNoIssues();

    [TestMethod]
    public void CreatedAtActionShouldUseRoutableActionName_ConflictingOptions() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Mvc;

            public static class MvcConfiguration
            {
                public static void ConfigureOne(MvcOptions options) =>
                    options.SuppressAsyncSuffixInActionNames = true;

                public static void ConfigureTwo(MvcOptions options) =>
                    options.SuppressAsyncSuffixInActionNames = false;
            }

            public class OrdersController : ControllerBase
            {
                public IActionResult Create() =>
                    CreatedAtAction(nameof(GetAsync), new { id = 1 }, new object());

                public IActionResult GetAsync() => Ok();
            }
            """).VerifyNoIssues();

    [TestMethod]
    public void CreatedAtActionShouldUseRoutableActionName_UnknownOptionRetainsDefault() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Mvc;

            public static class MvcConfiguration
            {
                public static void Configure(MvcOptions options, bool suppress) =>
                    options.SuppressAsyncSuffixInActionNames = suppress;
            }

            public class OrdersController : ControllerBase
            {
                public IActionResult Create() =>
                    CreatedAtAction(nameof(GetAsync), new { id = 1 }, new object()); // Noncompliant

                public IActionResult GetAsync() => Ok();
            }
            """).Verify();

    [TestMethod]
    public void CreatedAtActionShouldUseRoutableActionName_Lookalikes() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Mvc;

            public class Lookalike
            {
                public object CreatedAtAction(string actionName, object routeValues, object value) => null;
                public object Build() => CreatedAtAction(nameof(TargetAsync), new { id = 1 }, new object());
                public object TargetAsync() => null;
            }

            public class OrdersController : ControllerBase
            {
                public IActionResult Create() =>
                    CreatedAtAction(nameof(HelperAsync), new { id = 1 }, new object());

                [NonAction]
                public IActionResult HelperAsync() => Ok();
            }
            """).VerifyNoIssues();
}

#endif
