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
public class HttpCallShouldNotBeMadeInALoopTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.HttpCallShouldNotBeMadeInALoop>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        using System;
        using System.Collections.Generic;
        using System.Net.Http;
        using System.Net.Http.Json;
        using System.Threading.Tasks;

        namespace System.Net.Http
        {
            public class HttpClient
            {
                public Task<string> GetStringAsync(string url) => null;
                public void CancelPendingRequests() { }
            }
        }

        namespace System.Net.Http.Json
        {
            public static class HttpClientJsonExtensions
            {
                public static Task<T> GetFromJsonAsync<T>(this HttpClient client, string url) => null;
            }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public sealed class HttpGetAttribute : Attribute { }
            public abstract class ControllerBase { }
        }

        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static void MapGet(
                    this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints,
                    string pattern,
                    Func<Task> handler) { }
            }
        }

        """;

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_NoncompliantInForEach() =>
        builder.AddSnippet(
            Stubs + """
            public class Client : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async Task GetAll(HttpClient client, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        await client.GetStringAsync($"/items/{id}"); // Noncompliant {{This HTTP call directly depends on the loop variable and runs once per iteration - batch the requests or move the call outside the loop.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_NoncompliantInInlineMinimalApiHandler() =>
        builder.AddSnippet(
            Stubs + """
            public static class Endpoints
            {
                public static void Map(
                    Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app,
                    HttpClient client,
                    IEnumerable<int> ids)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/items", async () =>
                    {
                        foreach (var id in ids)
                        {
                            await client.GetStringAsync($"/items/{id}"); // Noncompliant
                        }
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_NoncompliantInFor() =>
        builder.AddSnippet(
            Stubs + """
            public class Client : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async Task GetAll(HttpClient client, int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        await client.GetStringAsync($"/items/{i}"); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_NoncompliantInHelperReachedFromAction() =>
        builder.AddSnippet(
            Stubs + """
            public class Client : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Task GetAll(HttpClient client, IEnumerable<int> ids) => Load(client, ids);

                private static async Task Load(HttpClient client, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        await client.GetStringAsync($"/items/{id}"); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantInWhileWithoutLoopDeclaredRequestValue() =>
        builder.AddSnippet(
            Stubs + """
            public class Client
            {
                public async Task GetAll(HttpClient client, int count)
                {
                    var i = 0;
                    while (i < count)
                    {
                        await client.GetStringAsync($"/items/{i}");
                        i++;
                    }
                }
            }
            """)
            .VerifyNoIssues();

    // N+1: the loop iterates what another request returned, so the fan-out is visible in the method itself and the
    // rule does not need the caller to be an API action.
    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_ReportsRequestPerElementOfAFetchedSequence() =>
        builder.AddSnippet(
            Stubs + """
            public class Synchronizer
            {
                public async Task Sync(HttpClient client)
                {
                    var ids = await client.GetFromJsonAsync<List<int>>("/items");
                    foreach (var id in ids)
                    {
                        await client.GetStringAsync($"/items/{id}"); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantWhenTheSequenceIsNotFetched() =>
        builder.AddSnippet(
            Stubs + """
            public class Synchronizer
            {
                public async Task Sync(HttpClient client, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        await client.GetStringAsync($"/items/{id}");
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantForRetryLoop() =>
        builder.AddSnippet(
            Stubs + """
            public class Client
            {
                public async Task<string> GetWithRetry(HttpClient client, string url, int maxAttempts)
                {
                    for (int attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        try
                        {
                            return await client.GetStringAsync(url);
                        }
                        catch (Exception)
                        {
                            await Task.Delay(100);
                        }
                    }
                    return null;
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantForIndirectLoopVariableDependency() =>
        builder.AddSnippet(
            Stubs + """
            public class Client
            {
                public async Task GetAll(HttpClient client, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        var url = $"/items/{id}";
                        await client.GetStringAsync(url);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantSingleCallOutsideLoop() =>
        builder.AddSnippet(
            Stubs + """
            public class Client
            {
                public async Task<string> GetOne(HttpClient client, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        Use(id);
                    }
                    return await client.GetStringAsync("/items/batch");
                }

                private static void Use(int id) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantWhenCallIsInsideLambdaScheduledFromLoop() =>
        builder.AddSnippet(
            Stubs + """
            public class Client
            {
                public void GetAll(HttpClient client, IEnumerable<int> ids)
                {
                    var tasks = new List<Task<string>>();
                    foreach (var id in ids)
                    {
                        tasks.Add(Task.Run(() => client.GetStringAsync($"/items/{id}").Result));
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantWhenCallIsInsideLocalFunctionInvokedFromLoop() =>
        builder.AddSnippet(
            Stubs + """
            public class Client
            {
                public async Task GetAll(HttpClient client, IEnumerable<int> ids)
                {
                    async Task<string> Get(int id) => await client.GetStringAsync($"/items/{id}");

                    foreach (var id in ids)
                    {
                        Use(await Get(id));
                    }
                }

                private static void Use(string value) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantForNonHttpCallInsideLoop() =>
        builder.AddSnippet(
            Stubs + """
            public class Client
            {
                public void Process(IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        Use(id);
                    }
                }

                private static void Use(int id) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantForCancelPendingRequests() =>
        builder.AddSnippet(
            Stubs + """
            public class Client
            {
                public void Cancel(HttpClient client, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        client.CancelPendingRequests();
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpCallShouldNotBeMadeInALoop_CompliantForCustomHttpClientExtension() =>
        builder.AddSnippet(
            Stubs + """
            public static class CustomHttpExtensions
            {
                public static void Inspect(this HttpClient client) { }
            }

            public class Client
            {
                public void InspectAll(HttpClient client, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        client.Inspect();
                    }
                }
            }
            """)
            .VerifyNoIssues();
}
