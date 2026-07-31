using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class SharedDictionariesShouldUseJunoDictionariesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.SharedDictionariesShouldUseJunoDictionaries>();

    [TestMethod]
    public void SharedDictionariesShouldUseJunoDictionaries_NoncompliantDirectSkidblandirGet() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading.Tasks;

            namespace System.Net.Http
            {
                public class HttpResponseMessage { }

                public class HttpClient
                {
                    public Task<string> GetStringAsync(string url) => Task.FromResult(string.Empty);
                    public Task<HttpResponseMessage> GetAsync(string url) => Task.FromResult(new HttpResponseMessage());
                }
            }

            public class Service
            {
                private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

                public async Task<string> Fetch()
                {
                    return await _httpClient.GetStringAsync("https://skidblandir.pracuj.local/api/dictionaries/categories"); // Noncompliant {{Use Juno.Dictionaries instead of calling Skidblandir API directly.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void SharedDictionariesShouldUseJunoDictionaries_NoncompliantDirectSkidblandirGetAsync() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading.Tasks;

            namespace System.Net.Http
            {
                public class HttpResponseMessage { }

                public class HttpClient
                {
                    public Task<string> GetStringAsync(string url) => Task.FromResult(string.Empty);
                    public Task<HttpResponseMessage> GetAsync(string url) => Task.FromResult(new HttpResponseMessage());
                }
            }

            public class Service
            {
                private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

                public async Task<System.Net.Http.HttpResponseMessage> Fetch()
                {
                    return await _httpClient.GetAsync("https://api.company/pl/skidblandir/v1/categories"); // Noncompliant {{Use Juno.Dictionaries instead of calling Skidblandir API directly.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void SharedDictionariesShouldUseJunoDictionaries_CompliantForNonSkidblandirHttpCall() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading.Tasks;

            namespace System.Net.Http
            {
                public class HttpResponseMessage { }

                public class HttpClient
                {
                    public Task<string> GetStringAsync(string url) => Task.FromResult(string.Empty);
                    public Task<HttpResponseMessage> GetAsync(string url) => Task.FromResult(new HttpResponseMessage());
                }
            }

            public class Service
            {
                private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

                public async Task<string> Fetch()
                {
                    return await _httpClient.GetStringAsync("https://example.org/api/config");
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void SharedDictionariesShouldUseJunoDictionaries_CompliantForJunoDictionaryFacade() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            namespace GP.Juno.Dictionaries
            {
                public interface IDictionaryReader
                {
                    Task<string> GetAsync(string dictionaryName);
                }
            }

            public class Service
            {
                private readonly GP.Juno.Dictionaries.IDictionaryReader _reader;

                public Service(GP.Juno.Dictionaries.IDictionaryReader reader) => _reader = reader;

                public Task<string> Fetch() => _reader.GetAsync("categories");
            }
            """)
            .VerifyNoIssues();
}
