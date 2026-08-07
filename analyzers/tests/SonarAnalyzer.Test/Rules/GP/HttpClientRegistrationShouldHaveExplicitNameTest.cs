using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class HttpClientRegistrationShouldHaveExplicitNameTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.HttpClientRegistrationShouldHaveExplicitName>();

    private const string HttpClientStubs =
        """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }
            public interface IHttpClientBuilder { }

            public static class HttpClientFactoryServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services) => null;
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null;
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null;
            }
        }
        """;

    [TestMethod]
    public void HttpClientRegistrationShouldHaveExplicitName_NoncompliantForDefaultClientWithJuno() =>
        builder.AddSnippet(
            HttpClientStubs + """

            namespace GP.Juno.HttpApiClient.HttpSending
            {
                public interface IHttpSenderFactory { }
            }

            namespace App
            {
                using Microsoft.Extensions.DependencyInjection;

                public class Startup
                {
                    public void Configure(IServiceCollection services) =>
                        services.AddHttpClient(); // Noncompliant {{Give this HTTP client an explicit name; Juno does not support the default client registration.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpClientRegistrationShouldHaveExplicitName_NoncompliantForStaticCall() =>
        builder.AddSnippet(
            HttpClientStubs + """

            namespace GP.Juno.HttpApiClient.HttpSending
            {
                public interface IHttpSenderFactory { }
            }

            public class Startup
            {
                public void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
                    Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient(services); // Noncompliant {{Give this HTTP client an explicit name; Juno does not support the default client registration.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpClientRegistrationShouldHaveExplicitName_CompliantForNamedAndTypedClients() =>
        builder.AddSnippet(
            HttpClientStubs + """

            namespace GP.Juno.HttpApiClient.HttpSending
            {
                public interface IHttpSenderFactory { }
            }

            namespace App
            {
                using Microsoft.Extensions.DependencyInjection;

                public class ApiClient { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddHttpClient("orders");
                        services.AddHttpClient<ApiClient>();
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpClientRegistrationShouldHaveExplicitName_CompliantWithoutJuno() =>
        builder.AddSnippet(
            HttpClientStubs + """

            namespace App
            {
                using Microsoft.Extensions.DependencyInjection;

                public class Startup
                {
                    public void Configure(IServiceCollection services) =>
                        services.AddHttpClient();
                }
            }
            """)
            .VerifyNoIssues();
}
