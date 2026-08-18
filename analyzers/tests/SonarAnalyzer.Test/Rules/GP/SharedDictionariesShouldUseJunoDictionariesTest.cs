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
    public void SharedDictionariesShouldUseJunoDictionaries_NoncompliantDirectJunoHttpClientBuilder() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;
            using GP.Juno.HttpClient;

            // Shape matches the real GP.Juno.HttpClient fluent chain: IHttpClientBuilder.Service(name) -> HttpRequestProperties.AddPath(path) -> ...GetJson<T>().
            namespace GP.Juno.HttpClient
            {
                public interface IHttpClientBuilder { }

                public class HttpRequestProperties
                {
                    public HttpRequestProperties AddPath(string path) => this;

                    public Task<T> GetJson<T>() => Task.FromResult(default(T));
                }

                public static class HttpClientBuilderExtensions
                {
                    public static HttpRequestProperties Service(this IHttpClientBuilder builder, string name) => new HttpRequestProperties();
                }
            }

            public class Service
            {
                private readonly GP.Juno.HttpClient.IHttpClientBuilder _clientBuilder;

                public Service(GP.Juno.HttpClient.IHttpClientBuilder clientBuilder) => _clientBuilder = clientBuilder;

                public Task<string> Fetch() =>
                    _clientBuilder.Service("skidblandir").AddPath("/api/dictionaries/categories").GetJson<string>(); // Noncompliant {{Use Juno.Dictionaries instead of calling Skidblandir API directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void SharedDictionariesShouldUseJunoDictionaries_CompliantForLookalikeFluentApi() =>
        builder.AddSnippet(
            """
            public sealed class QueryBuilder
            {
                public QueryBuilder Service(string name) => this;
                public QueryBuilder AddPath(string path) => this;
                public T GetJson<T>() => default(T);
            }

            public class Service
            {
                private readonly QueryBuilder _builder = new QueryBuilder();

                public string Fetch() =>
                    _builder.Service("skidblandir").AddPath("/api/dictionaries/categories").GetJson<string>();
            }
            """)
            .VerifyNoIssues();

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
    public void SharedDictionariesShouldUseJunoDictionaries_CompliantWhenPayloadMentionsSkidblandir() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            namespace System.Net.Http
            {
                public class StringContent
                {
                    public StringContent(string content) { }
                }

                public class HttpClient
                {
                    public Task<string> PostAsync(string requestUri, StringContent content) => Task.FromResult(string.Empty);
                }
            }

            public class Service
            {
                private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

                public Task<string> SendAuditEntry() =>
                    _httpClient.PostAsync(
                        "https://example.org/api/audit",
                        new System.Net.Http.StringContent("Migrated from skidblandir to Juno"));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void SharedDictionariesShouldUseJunoDictionaries_CompliantForJunoDictionaryFacade() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            // Shape matches the real GP.Juno.Abstractions.Dictionaries.IDictionaries facade.
            namespace GP.Juno.Abstractions.Dictionaries
            {
                public class DictionaryName
                {
                    public static implicit operator DictionaryName(string name) => new DictionaryName();
                }

                public class Parameters { }

                public interface IDictionaries
                {
                    Task<IReadOnlyCollection<T>> GetItems<T>(DictionaryName dictionaryName, Parameters parameters, CancellationToken cancellation);
                }
            }

            public class Service
            {
                private readonly GP.Juno.Abstractions.Dictionaries.IDictionaries _dictionaries;

                public Service(GP.Juno.Abstractions.Dictionaries.IDictionaries dictionaries) => _dictionaries = dictionaries;

                public Task<IReadOnlyCollection<string>> Fetch() =>
                    _dictionaries.GetItems<string>("categories", new GP.Juno.Abstractions.Dictionaries.Parameters(), CancellationToken.None);
            }
            """)
            .VerifyNoIssues();
}
