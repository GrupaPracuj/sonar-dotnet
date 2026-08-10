using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotSendRequestToUserControlledUrlTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotSendRequestToUserControlledUrl>();

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
                protected IActionResult Ok(object value) => null;
                protected IActionResult BadRequest() => null;
            }
        }

        namespace System.Net.Http
        {
            public class HttpMethod
            {
                public static HttpMethod Get => null;
            }

            public class HttpRequestMessage
            {
                public HttpRequestMessage() { }
                public HttpRequestMessage(HttpMethod method, string requestUri) { }
                public System.Uri RequestUri { get; set; }
            }

            public class HttpClient
            {
                public string GetStringAsync(string requestUri) => null;
                public string GetStringAsync(System.Uri requestUri) => null;
                public string PostAsync(string requestUri, object content) => null;
                public string Send(HttpRequestMessage request) => null;
                public string SendAsync(HttpRequestMessage request) => null;
            }

            public class HttpMessageInvoker
            {
                public string SendAsync(HttpRequestMessage request) => null;
            }
        }

        namespace System.Net
        {
            public class WebClient
            {
                public string DownloadString(string address) => null;
            }

            public abstract class WebRequest
            {
                public static WebRequest Create(string requestUriString) => null;
            }
        }
        """;

    private const string MinimalApiStubs =
        """

        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static void MapGet<T, TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, TResult> handler) { }
                public static void MapGet<TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<TResult> handler) { }
                public static void MapPost<T, TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, TResult> handler) { }
                public static void MapPut<T, TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, TResult> handler) { }
                public static void MapPatch<T, TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, TResult> handler) { }
                public static void MapDelete<T, TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, TResult> handler) { }
                public static void MapMethods<T, TResult>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, string[] httpMethods, System.Func<T, TResult> handler) { }
            }
        }
        """;

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_NoncompliantForActionParameter() =>
        builder.AddSnippet(
            Stubs + """

            public class PreviewController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Preview(string imageUrl)
                {
                    var content = _client.GetStringAsync(imageUrl); // Noncompliant {{Do not send a request to a URL taken from parameter 'imageUrl' - validate the host against an allowlist first.}}
                    return Ok(content);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_NoncompliantForHttpRequestMessageConstructor() =>
        builder.AddSnippet(
            Stubs + """

            public class PreviewController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Preview(string imageUrl)
                {
                    var content = _client.SendAsync(new System.Net.Http.HttpRequestMessage(
                        requestUri: imageUrl, // Noncompliant {{Do not send a request to a URL taken from parameter 'imageUrl' - validate the host against an allowlist first.}}
                        method: System.Net.Http.HttpMethod.Get));
                    return Ok(content);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_NoncompliantForHttpRequestMessageInitializer() =>
        builder.AddSnippet(
            Stubs + """

            public class PreviewController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly System.Net.Http.HttpMessageInvoker _client = new System.Net.Http.HttpMessageInvoker();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Preview(string imageUrl)
                {
                    var content = _client.SendAsync(new System.Net.Http.HttpRequestMessage
                    {
                        RequestUri = new System.Uri(imageUrl) // Noncompliant
                    });
                    return Ok(content);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_NoncompliantForSynchronousSend() =>
        builder.AddSnippet(
            Stubs + """

            public class PreviewController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Preview(string imageUrl)
                {
                    var content = _client.Send(new System.Net.Http.HttpRequestMessage(
                        System.Net.Http.HttpMethod.Get,
                        imageUrl)); // Noncompliant {{Do not send a request to a URL taken from parameter 'imageUrl' - validate the host against an allowlist first.}}
                    return Ok(content);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_CompliantForConstantHttpRequestMessageUri() =>
        builder.AddSnippet(
            Stubs + """

            public class PreviewController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Preview(string imageUrl) =>
                    Ok(_client.SendAsync(new System.Net.Http.HttpRequestMessage
                    {
                        RequestUri = new System.Uri("https://images.internal/image")
                    }));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_NoncompliantForInterpolatedActionParameter() =>
        builder.AddSnippet(
            Stubs + """

            public class PreviewController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Preview(string host)
                {
                    var content = _client.GetStringAsync($"https://{host}/image"); // Noncompliant {{Do not send a request to a URL taken from parameter 'host' - validate the host against an allowlist first.}}
                    return Ok(content);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_NoncompliantForWebClient() =>
        builder.AddSnippet(
            Stubs + """

            public class PreviewController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Preview(string address)
                {
                    var client = new System.Net.WebClient();
                    return Ok(client.DownloadString(address)); // Noncompliant {{Do not send a request to a URL taken from parameter 'address' - validate the host against an allowlist first.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_CompliantForUrlBuiltFromConfiguration() =>
        builder.AddSnippet(
            Stubs + """

            public class PreviewController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();
                private readonly string _baseUrl = "https://images.internal";

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Preview(int imageId)
                {
                    var content = _client.GetStringAsync($"{_baseUrl}/images/{imageId}");
                    return Ok(content);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_CompliantForPayloadArgument() =>
        builder.AddSnippet(
            Stubs + """

            public class PreviewController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Send(string comment)
                {
                    var result = _client.PostAsync("https://images.internal/comments", comment);
                    return Ok(result);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_CompliantOutsideController() =>
        builder.AddSnippet(
            Stubs + """

            public class ImageFetcher
            {
                private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();

                public string Fetch(string imageUrl) => _client.GetStringAsync(imageUrl);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_MinimalApiNoncompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    var client = new System.Net.Http.HttpClient();
                    var invoker = new System.Net.Http.HttpMessageInvoker();
                    var webClient = new System.Net.WebClient();

                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/get",
                        (string url) => client.GetStringAsync(url)); // Noncompliant {{Do not send a request to a URL taken from parameter 'url' - validate the host against an allowlist first.}}
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(app, "/post",
                        (string host) => client.GetStringAsync($"https://{host}/resource")); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPut(app, "/put",
                        (string address) => webClient.DownloadString(address)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPatch(app, "/patch",
                        (string requestUri) => System.Net.WebRequest.Create(requestUri)); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapDelete(app, "/delete",
                        (string requestUri) => invoker.SendAsync(new System.Net.Http.HttpRequestMessage(
                            System.Net.Http.HttpMethod.Get, requestUri))); // Noncompliant
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(app, "/methods", new[] { "GET", "POST" },
                        (string requestUri) => client.SendAsync(new System.Net.Http.HttpRequestMessage
                        {
                            RequestUri = new System.Uri(requestUri) // Noncompliant
                        }));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/sync-send",
                        (string requestUri) => client.Send(new System.Net.Http.HttpRequestMessage(
                            System.Net.Http.HttpMethod.Get, requestUri))); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_MinimalApiFixedDestinationIsCompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    var client = new System.Net.Http.HttpClient();

                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/concat",
                        (string id) => client.GetStringAsync("https://images.internal/images/" + id));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(app, "/interpolation",
                        (string id) => client.GetStringAsync($"https://images.internal/images/{id}"));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/query",
                        (string query) => client.GetStringAsync("https://images.internal?q=" + query));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/fragment",
                        (string fragment) => client.GetStringAsync($"https://images.internal#{fragment}"));
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotSendRequestToUserControlledUrl_MinimalApiBoundariesAreCompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            namespace Custom
            {
                public static class Endpoints
                {
                    public static void MapGet<T, TResult>(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string pattern, System.Func<T, TResult> handler) { }
                }
            }

            public static class Endpoints
            {
                private static readonly System.Net.Http.HttpClient Client = new System.Net.Http.HttpClient();

                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string registrationUrl)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/nested",
                        (string url) =>
                        {
                            System.Func<string> nested = () => Client.GetStringAsync(url);
                            return nested();
                        });
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/local",
                        (string url) =>
                        {
                            string Fetch() => Client.GetStringAsync(url);
                            return Fetch();
                        });
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app,
                        Client.GetStringAsync(registrationUrl),
                        () => "ok");
                    Custom.Endpoints.MapGet(app, "/lookalike",
                        (string url) => Client.GetStringAsync(url));
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet<string, string>(app, "/named", Fetch);
                }

                private static string Fetch(string url) => Client.GetStringAsync(url);
            }
            """)
            .VerifyNoIssues();
}
