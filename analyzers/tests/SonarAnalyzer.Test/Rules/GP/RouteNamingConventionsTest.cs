/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class RouteNamingConventionsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.RouteNamingConventions>();

    [TestMethod]
    public void RouteNamingConventions_NoncompliantPascalCaseSegment() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class JobOffersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("JobOffers")] // Noncompliant {{Rename route segment 'JobOffers' to kebab-case.}}
                public void GetAll() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_NoncompliantCamelCaseSegment() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class JobOffersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("jobOffers")] // Noncompliant {{Rename route segment 'jobOffers' to kebab-case.}}
                public void GetAll() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_NoncompliantSnakeCaseSegment() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class JobOffersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("job_offers")] // Noncompliant {{Rename route segment 'job_offers' to kebab-case.}}
                public void GetAll() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_NoncompliantVerbWithSeparator() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class OrdersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("get-orders")] // Noncompliant {{Remove the verb 'get' from the route; the HTTP method already expresses the action.}}
                public void GetAll() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_NoncompliantVerbPascalCase() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class OrdersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("GetOrders")] // Noncompliant {{Remove the verb 'Get' from the route; the HTTP method already expresses the action.}}
                                                                 // Noncompliant@-1 {{Rename route segment 'GetOrders' to kebab-case.}}
                public void GetAll() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_NoncompliantTrailingSlash() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class RouteAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public RouteAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class UsersController
            {
                [Microsoft.AspNetCore.Mvc.Route("api/users/")] // Noncompliant {{Remove the trailing slash from the route.}}
                public void GetAll() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_KebabCase_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("RouteNamingConventions_KebabCase.cs")
            .WithCodeFix<CS.RouteSegmentShouldBeKebabCaseCodeFix>()
            .WithCodeFixedPaths("RouteNamingConventions_KebabCase.Fixed.cs")
            .VerifyCodeFix();

    [TestMethod]
    public void RouteNamingConventions_TrailingSlash_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("RouteNamingConventions_TrailingSlash.cs")
            .WithCodeFix<CS.RouteShouldNotHaveTrailingSlashCodeFix>()
            .WithCodeFixedPaths("RouteNamingConventions_TrailingSlash.Fixed.cs")
            .VerifyCodeFix();

    [TestMethod]
    public void RouteNamingConventions_NoncompliantOnClassLevelAttribute() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class RouteAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public RouteAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            [Microsoft.AspNetCore.Mvc.Route("JobOffers")] // Noncompliant {{Rename route segment 'JobOffers' to kebab-case.}}
            public class JobOffersController
            {
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_CompliantKebabCaseWithParameter() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class JobOffersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("job-offers/{id}")]
                public void Get(int id) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void RouteNamingConventions_CompliantControllerToken() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class RouteAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public RouteAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            [Microsoft.AspNetCore.Mvc.Route("api/[controller]")]
            public class JobOffersController
            {
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void RouteNamingConventions_CompliantWordStartingWithVerbPrefixButNotAWordBoundary() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class AddressesController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("addresses")]
                public void GetAll() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void RouteNamingConventions_CompliantValidateOperation() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpPostAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpPostAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class OrdersController
            {
                [Microsoft.AspNetCore.Mvc.HttpPost("orders/{id}/validate-payment")]
                public void ValidatePayment(int id) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void RouteNamingConventions_CompliantWorkflowActions() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpPostAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpPostAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class OrdersController
            {
                [Microsoft.AspNetCore.Mvc.HttpPost("orders/{id}/cancel")]
                public void Cancel(int id) { }

                [Microsoft.AspNetCore.Mvc.HttpPost("orders/{id}/approve")]
                public void Approve(int id) { }
            }
            """)
            .VerifyNoIssues();

    // A segment that is not entirely literal - an API-versioned "v{version}", a literal prefix in front of a
    // parameter, a replacement token with a suffix - is a normal template, not a casing mistake.
    [TestMethod]
    public void RouteNamingConventions_CompliantSegmentsContainingParametersOrTokens() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class RouteAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public RouteAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }

                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            [Microsoft.AspNetCore.Mvc.Route("api/v{version:apiVersion}/[controller]")]
            public class JobOffersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("order-{id}")]
                public void Get(int id) { }

                [Microsoft.AspNetCore.Mvc.HttpGet("[action]-archive")]
                public void Archive() { }
            }
            """)
            .VerifyNoIssues();

    // A segment naming a file is a route, not a casing mistake - but its casing is still checked.
    [TestMethod]
    public void RouteNamingConventions_FileNameSegment() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class DocsController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("swagger/v1/swagger.json")]
                public void Definition() { }

                [Microsoft.AspNetCore.Mvc.HttpGet("swagger/v1/Swagger.Json")] // Noncompliant {{Rename route segment 'Swagger.Json' to kebab-case.}}
                public void WronglyCasedDefinition() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_NoncompliantSecretRouteParameter() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class SessionsController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("sessions/{token}")] // Noncompliant {{Route parameter 'token' looks like it carries a secret - it will end up in server logs, browser history and proxy caches.}}
                public void GetByToken(string token) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_NoncompliantSecretRouteParameterModifiers() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class SessionsController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("sessions/{token?}")] // Noncompliant {{Route parameter 'token' looks like it carries a secret - it will end up in server logs, browser history and proxy caches.}}
                public void Optional(string token) { }

                [Microsoft.AspNetCore.Mvc.HttpGet("sessions/{*apiKey}")] // Noncompliant {{Route parameter 'apiKey' looks like it carries a secret - it will end up in server logs, browser history and proxy caches.}}
                public void CatchAll(string apiKey) { }

                [Microsoft.AspNetCore.Mvc.HttpGet("sessions/{**password}")] // Noncompliant {{Route parameter 'password' looks like it carries a secret - it will end up in server logs, browser history and proxy caches.}}
                public void DoubleCatchAll(string password) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void RouteNamingConventions_CompliantOrdinaryRouteParameter() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class UsersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("users/{id}")]
                public void Get(int id) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void RouteNamingConventions_CompliantPluralNounsSharingPrefixWithExpandedVerbs() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                public interface IRouteTemplateProvider
                {
                    string Template { get; }
                    int? Order { get; }
                    string Name { get; }
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
                {
                    public HttpGetAttribute(string template) => Template = template;
                    public string Template { get; }
                    public int? Order { get; set; }
                    public string Name { get; set; }
                }
            }

            public class ChecksController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("checks")]
                public void GetAll1() { }

                [Microsoft.AspNetCore.Mvc.HttpGet("searches")]
                public void GetAll2() { }

                [Microsoft.AspNetCore.Mvc.HttpGet("confirmations")]
                public void GetAll3() { }
            }
            """)
            .VerifyNoIssues();

    // The kebab-case fix has to rename the segment its own diagnostic points at. With two offending segments in one
    // template, always taking the first one would rename the wrong segment for the second issue - and the file-based
    // code fix verification cannot reach that, because it only ever applies the first diagnostic's action (and the
    // FixAll location is widened to the whole literal, where the offset is gone), so the selection is checked here.
    [DataTestMethod]
    [DataRow(0, "JobOffers")]
    [DataRow(4, "JobOffers")]
    [DataRow(10, "ByRecruiter")]
    [DataRow(20, "ByRecruiter")]
    public void RouteNamingConventions_KebabCaseFix_TargetsTheReportedSegment(int reportedOffset, string expected) =>
        CS.RouteLiteralCodeFixHelper.OffendingSegment("JobOffers/ByRecruiter", reportedOffset).Segment.Should().Be(expected);

    // A widened FixAll location no longer points at a segment, so the first offender is the only choice left.
    [TestMethod]
    public void RouteNamingConventions_KebabCaseFix_FallsBackToTheFirstOffenderWithoutAnOffset() =>
        CS.RouteLiteralCodeFixHelper.OffendingSegment("JobOffers/ByRecruiter", null).Segment.Should().Be("JobOffers");

    // An offset inside a compliant segment belongs to no offender, and then there is nothing to fix.
    [TestMethod]
    public void RouteNamingConventions_KebabCaseFix_FindsNothingForACompliantSegment() =>
        CS.RouteLiteralCodeFixHelper.OffendingSegment("api/ByRecruiter", 0).Segment.Should().BeNull();

    // The offset the analyzer reports is relative to the literal's value text, so the opening quote has to be skipped;
    // a span covering the whole literal - what FixAll widens to - is not a sub-range and yields no offset at all.
    [TestMethod]
    public void RouteNamingConventions_KebabCaseFix_ValueOffsetSkipsTheOpeningQuote()
    {
        var token = ((LiteralExpressionSyntax)SyntaxFactory.ParseExpression("""
            "api/ByRecruiter"
            """)).Token;

        CS.RouteLiteralCodeFixHelper.ValueOffset(token, new TextSpan(token.SpanStart + 1 + 4, "ByRecruiter".Length)).Should().Be(4);
        CS.RouteLiteralCodeFixHelper.ValueOffset(token, token.Span).Should().BeNull();
    }
}
