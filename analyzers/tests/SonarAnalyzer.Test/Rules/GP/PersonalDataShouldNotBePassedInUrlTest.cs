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
public class PersonalDataShouldNotBePassedInUrlTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PersonalDataShouldNotBePassedInUrl>();

    [TestMethod]
    public void PersonalDataShouldNotBePassedInUrl_NoncompliantMvcRouteParameters() =>
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
                [Microsoft.AspNetCore.Mvc.HttpGet("users/{email}")] // Noncompliant {{URL parameter 'email' looks like it carries personal data - it will end up in server logs, browser history and proxy caches.}}
                public void ByEmail(string email) { }

                [Microsoft.AspNetCore.Mvc.HttpGet("people/{dateOfBirth}")] // Noncompliant {{URL parameter 'dateOfBirth' looks like it carries personal data - it will end up in server logs, browser history and proxy caches.}}
                public void ByBirthDate(string dateOfBirth) { }

                [Microsoft.AspNetCore.Mvc.HttpGet("accounts/{username}")] // Noncompliant {{URL parameter 'username' looks like it carries personal data - it will end up in server logs, browser history and proxy caches.}}
                public void ByUsername(string username) { }

                [Microsoft.AspNetCore.Mvc.HttpGet("logins/{login}")] // Noncompliant {{URL parameter 'login' looks like it carries personal data - it will end up in server logs, browser history and proxy caches.}}
                public void ByLogin(string login) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void PersonalDataShouldNotBePassedInUrl_NoncompliantMinimalApiRouteParameter() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Routing
            {
                public interface IEndpointRouteBuilder { }
                public sealed class RouteHandlerBuilder { }
            }

            namespace Microsoft.AspNetCore.Builder
            {
                public static class EndpointRouteBuilderExtensions
                {
                    public static Microsoft.AspNetCore.Routing.RouteHandlerBuilder MapGet(
                        this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints,
                        string pattern,
                        System.Delegate handler) => new Microsoft.AspNetCore.Routing.RouteHandlerBuilder();
                }
            }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(
                        app,
                        "users/{phone}", // Noncompliant {{URL parameter 'phone' looks like it carries personal data - it will end up in server logs, browser history and proxy caches.}}
                        (System.Action)(() => { }));
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void PersonalDataShouldNotBePassedInUrl_NoncompliantExplicitQueryParameter() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public sealed class FromQueryAttribute : System.Attribute
                {
                    public string Name { get; set; }
                }
            }

            public class UsersController
            {
                public void ByEmail(
                    [Microsoft.AspNetCore.Mvc.FromQuery] string email) { } // Noncompliant {{URL parameter 'email' looks like it carries personal data - it will end up in server logs, browser history and proxy caches.}}

                public void ByPhone(
                    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "phone")] string value) { } // Noncompliant {{URL parameter 'phone' looks like it carries personal data - it will end up in server logs, browser history and proxy caches.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PersonalDataShouldNotBePassedInUrl_CompliantIdentifiersAndMetadata() =>
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

                public sealed class FromQueryAttribute : System.Attribute
                {
                    public string Name { get; set; }
                }
            }

            public class UsersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet("users/{id}/history/{emailId}/{birthDateReference}")]
                public void History(
                    string id,
                    string emailId,
                    string birthDateReference,
                    [Microsoft.AspNetCore.Mvc.FromQuery] int phoneCount) { }
            }
            """)
            .VerifyNoIssues();
}
