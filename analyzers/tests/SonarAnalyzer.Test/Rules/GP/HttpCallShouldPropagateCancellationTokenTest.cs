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
}
