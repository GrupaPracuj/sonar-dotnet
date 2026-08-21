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
public class DoNotCreateFrameworkHttpClientTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotCreateFrameworkHttpClient>();

    private const string Stubs =
        """
        namespace System.Net.Http
        {
            public class HttpClient
            {
                public string GetStringAsync(string url) => null;
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
        }

        namespace System.Net
        {
            public class WebClient
            {
                public string DownloadString(string address) => null;
            }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok(object value) => null;
            }
        }
        """;

    [TestMethod]
    public void DoNotCreateFrameworkHttpClient_NoncompliantForHttpClientInService() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient(); // Noncompliant {{Obtain the HTTP client from Juno (IHttpClientBuilder.Service(...)) instead of creating 'HttpClient' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotCreateFrameworkHttpClient_NoncompliantForWebClient() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                public string Fetch() => new System.Net.WebClient().DownloadString("/orders"); // Noncompliant {{Obtain the HTTP client from Juno (IHttpClientBuilder.Service(...)) instead of creating 'WebClient' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotCreateFrameworkHttpClient_NoncompliantForHttpClientFactory() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                private readonly System.Net.Http.IHttpClientFactory _factory;

                public string Fetch() => _factory.CreateClient("orders").GetStringAsync("/orders"); // Noncompliant {{Obtain the HTTP client from Juno (IHttpClientBuilder.Service(...)) instead of creating 'IHttpClientFactory.CreateClient' directly.}}
            }
            """)
            .Verify();

    // A client created in a controller field initializer is not reported by S6962, so it is reported here.
    [TestMethod]
    public void DoNotCreateFrameworkHttpClient_NoncompliantForHttpClientInControllerField() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient(); // Noncompliant {{Obtain the HTTP client from Juno (IHttpClientBuilder.Service(...)) instead of creating 'HttpClient' directly.}}
            }
            """)
            .Verify();

    // Inside a controller action, S6962 already reports this shape.
    [TestMethod]
    public void DoNotCreateFrameworkHttpClient_CompliantInControllerActionCoveredByS6962() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    var client = new System.Net.Http.HttpClient();
                    return Ok(client.GetStringAsync("/orders"));
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotCreateFrameworkHttpClient_CompliantForResponseTypesFlowingThrough() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                public string Map(string response) => response;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotCreateFrameworkHttpClient_CompliantInsideJunoHttpClientImplementation() =>
        builder.AddSnippet(
            Stubs + """

            namespace GP.Juno.HttpClient
            {
                public sealed class HttpClientWrapper
                {
                    private System.Net.Http.HttpClient CreateHttpClient() =>
                        new System.Net.Http.HttpClient();
                }
            }
            """)
            .VerifyNoIssues();
}
