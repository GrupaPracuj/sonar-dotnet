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
}
