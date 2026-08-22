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
public class CancellationShouldNotBeOptedOutOfTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CancellationShouldNotBeOptedOutOf>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void CancellationShouldNotBeOptedOutOf_NoTokenInScope() =>
        builder.AddSnippet(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public class Client
            {
                private Task<string> Fetch(string path, CancellationToken cancellation) => Task.FromResult(path);

                public async Task<string> NoToken(string path) =>
                    await Fetch(path, CancellationToken.None); // Noncompliant {{This call can never be cancelled. Add a CancellationToken parameter to 'NoToken' and pass it here.}}

                public async Task<string> DefaultLiteral(string path) =>
                    await Fetch(path, default); // Noncompliant

                public async Task<string> DefaultExpression(string path) =>
                    await Fetch(path, default(CancellationToken)); // Noncompliant

                public async Task<string> NamedArgument(string path) =>
                    await Fetch(path, cancellation: CancellationToken.None); // Noncompliant
            }
            """).Verify();

    [TestMethod]
    public void CancellationShouldNotBeOptedOutOf_TokenAvailableIsGp0027() =>
        builder.AddSnippet(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public class Client
            {
                private Task<string> Fetch(string path, CancellationToken cancellation) => Task.FromResult(path);

                // A token is in scope, so forwarding it is the fix and GP0027 owns the finding.
                public async Task<string> TokenInScope(string path, CancellationToken cancellation) =>
                    await Fetch(path, CancellationToken.None);

                public async Task<string> Propagated(string path, CancellationToken cancellation) =>
                    await Fetch(path, cancellation);

                public async Task<string> Renamed(string path, CancellationToken ct) =>
                    await Fetch(path, default);
            }
            """).VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeOptedOutOf_Exceptions() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public class Disposable : IDisposable, IAsyncDisposable
            {
                private Task Flush(CancellationToken cancellation) => Task.CompletedTask;

                public void Dispose() => Flush(CancellationToken.None).GetAwaiter().GetResult();

                public async ValueTask DisposeAsync() => await Flush(CancellationToken.None);
            }

            public class Program
            {
                private static Task Warmup(CancellationToken cancellation) => Task.CompletedTask;

                public static async Task Main() => await Warmup(CancellationToken.None);

                private readonly Task _started = Warmup(CancellationToken.None);

                public Program() => Warmup(CancellationToken.None);
            }

            public class Registrations
            {
                private static Task Warmup(CancellationToken cancellation) => Task.CompletedTask;

                public void Register(Action<Func<Task>> add) =>
                    add(() => Warmup(CancellationToken.None));

                public Task LocalFunction()
                {
                    return Inner();

                    Task Inner() => Warmup(CancellationToken.None);
                }
            }
            """).VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeOptedOutOf_NotACancellationToken() =>
        builder.AddSnippet(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public class Client
            {
                private Task<string> Untokened(string path, string other) => Task.FromResult(path);
                private Task<string> Overloaded(string path) => Task.FromResult(path);

                public async Task<string> NoTokenParameter(string path) =>
                    await Untokened(path, default);

                public async Task<string> NoTokenAtAll(string path) =>
                    await Overloaded(path);
            }
            """).VerifyNoIssues();
}
