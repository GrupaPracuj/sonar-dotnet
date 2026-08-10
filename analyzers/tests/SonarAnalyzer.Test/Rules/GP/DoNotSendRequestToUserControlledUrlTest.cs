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
}
