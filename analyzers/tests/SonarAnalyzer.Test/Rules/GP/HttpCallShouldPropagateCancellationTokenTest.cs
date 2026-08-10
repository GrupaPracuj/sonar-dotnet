using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class HttpCallShouldPropagateCancellationTokenTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.HttpCallShouldPropagateCancellationToken>();

    private const string HttpClientStubs =
        """
        namespace System.Net.Http
        {
            public class HttpClient
            {
                public System.Threading.Tasks.Task<string> GetStringAsync(string url) => null;
                public System.Threading.Tasks.Task<string> GetStringAsync(string url, System.Threading.CancellationToken cancellationToken) => null;
            }
        }
        """;

    // Mirrors the real GP.Juno fluent HTTP API shape (namespaces, types and member signatures verified against the
    // submodules/juno source): IHttpClientBuilder.Service(string) returns a HttpRequestProperties, whose extension
    // methods (e.g. GetJson<T>) are the only way to actually send the request. None of these members has an overload
    // accepting a CancellationToken - as is the case for IHttpClient.Send, HttpRequestProperties.Get(Json)/Post(Json)/...
    // and every other member of this fluent chain in the real submodule.
    private const string JunoHttpClientStubs =
        """
        namespace GP.Juno.HttpClient
        {
            public interface IHttpClientBuilder
            {
            }

            public class HttpRequestProperties
            {
                public HttpRequestProperties(IHttpClientBuilder builder) { }
            }

            public static class HttpClientBuilderExtensions
            {
                public static HttpRequestProperties Service(this IHttpClientBuilder builder, string name) => new HttpRequestProperties(builder);

                public static System.Threading.Tasks.Task<string> GetJson<T>(this HttpRequestProperties requestProps) => null;
            }
        }
        """;

    [TestMethod]
    public void HttpCallShouldPropagateCancellationToken_NoncompliantWhenTokenIsAvailableButNotPassed() =>
        builder.AddSnippet(
            HttpClientStubs + """

            public class OrderClient
            {
                private readonly System.Net.Http.HttpClient _httpClient;

                public System.Threading.Tasks.Task<string> GetOrder(string id, System.Threading.CancellationToken cancellationToken) =>
                    _httpClient.GetStringAsync("/orders/" + id); // Noncompliant {{Pass the available CancellationToken to this call to another service, so it can be cancelled or time out.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpCallShouldPropagateCancellationToken_CompliantWhenTokenIsPassed() =>
        builder.AddSnippet(
            HttpClientStubs + """

            public class OrderClient
            {
                private readonly System.Net.Http.HttpClient _httpClient;

                public System.Threading.Tasks.Task<string> GetOrder(string id, System.Threading.CancellationToken cancellationToken) =>
                    _httpClient.GetStringAsync("/orders/" + id, cancellationToken);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldPropagateCancellationToken_NoncompliantForSuppressedTokens() =>
        builder.WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
            HttpClientStubs + """

            public class OrderClient
            {
                private readonly System.Net.Http.HttpClient _httpClient;

                public void GetOrders(System.Threading.CancellationToken cancellationToken)
                {
                    _httpClient.GetStringAsync("/orders", System.Threading.CancellationToken.None); // Noncompliant
                    _httpClient.GetStringAsync("/orders", default(System.Threading.CancellationToken)); // Noncompliant
                    _httpClient.GetStringAsync("/orders", new System.Threading.CancellationToken()); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpCallShouldPropagateCancellationToken_CompliantWhenOnlyUnrelatedOverloadHasToken() =>
        builder.AddSnippet(
            """
            namespace System.Net.Http
            {
                public class HttpClient
                {
                    public System.Threading.Tasks.Task<string> GetStringAsync(string url) => null;
                    public System.Threading.Tasks.Task<string> GetStringAsync(int requestId, System.Threading.CancellationToken cancellationToken) => null;
                }
            }

            public class OrderClient
            {
                private readonly System.Net.Http.HttpClient _httpClient;

                public System.Threading.Tasks.Task<string> GetOrder(System.Threading.CancellationToken cancellationToken) =>
                    _httpClient.GetStringAsync("/orders");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldPropagateCancellationToken_CompliantWhenNoTokenIsAvailable() =>
        builder.AddSnippet(
            HttpClientStubs + """

            public class OrderClient
            {
                private readonly System.Net.Http.HttpClient _httpClient;

                public System.Threading.Tasks.Task<string> GetOrder(string id) =>
                    _httpClient.GetStringAsync("/orders/" + id);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldPropagateCancellationToken_CompliantForJunoFluentBuilderWithoutCancellationTokenOverload() =>
        builder.AddSnippet(
            """
            using GP.Juno.HttpClient;

            """ + JunoHttpClientStubs + """

            public class OrderClient
            {
                private readonly IHttpClientBuilder _builder;

                public System.Threading.Tasks.Task<string> GetOrder(string id, System.Threading.CancellationToken cancellationToken) =>
                    _builder.Service("orders").GetJson<string>();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldPropagateCancellationToken_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("HttpCallShouldPropagateCancellationToken.cs")
            .WithCodeFix<CS.HttpCallShouldPropagateCancellationTokenCodeFix>()
            .WithCodeFixedPaths("HttpCallShouldPropagateCancellationToken.Fixed.cs")
            .VerifyCodeFix();
}
