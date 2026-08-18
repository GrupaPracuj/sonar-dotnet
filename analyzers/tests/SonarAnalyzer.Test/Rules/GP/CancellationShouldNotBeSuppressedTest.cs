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
public class CancellationShouldNotBeSuppressedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CancellationShouldNotBeSuppressed>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantForSwallowedOperationCanceled() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process(System.Threading.CancellationToken cancellationToken)
                {
                    try
                    {
                        Work(cancellationToken);
                    }
                    catch (System.OperationCanceledException) // Noncompliant {{Do not turn cancellation into success - let 'OperationCanceledException' propagate or rethrow it.}}
                    {
                        System.Console.WriteLine("Cancelled");
                    }
                }

                private void Work(System.Threading.CancellationToken cancellationToken) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantForTaskCanceledWithoutCallerToken() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Threading.Tasks.TaskCanceledException)
                    {
                        System.Console.WriteLine("HTTP timeout");
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantForSwallowedTaskCanceled() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process(System.Threading.CancellationToken cancellationToken)
                {
                    try
                    {
                        Work(cancellationToken);
                    }
                    catch (System.Threading.Tasks.TaskCanceledException) // Noncompliant {{Do not turn cancellation into success - let 'TaskCanceledException' propagate or rethrow it.}}
                    {
                    }
                }

                private void Work(System.Threading.CancellationToken cancellationToken) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantWhenRethrown() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.OperationCanceledException)
                    {
                        System.Console.WriteLine("Cancelled");
                        throw;
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantWhenThrowingCancellation() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.OperationCanceledException)
                    {
                        throw new System.Threading.Tasks.TaskCanceledException();
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantWhenThrowingAnotherException() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.OperationCanceledException) // Noncompliant
                    {
                        throw new System.InvalidOperationException();
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantWhenReturningAResult() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public bool Process()
                {
                    try
                    {
                        Work();
                        return true;
                    }
                    catch (System.OperationCanceledException) // Noncompliant
                    {
                        return false;
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantWhenBreakingOutOfALoop() =>
        builder.AddSnippet(
            """
            public class Worker
            {
                public void Run()
                {
                    while (true)
                    {
                        try
                        {
                            Work();
                        }
                        catch (System.OperationCanceledException) // Noncompliant
                        {
                            break;
                        }
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantForAsyncTaskReturn() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public async System.Threading.Tasks.Task Process()
                {
                    try
                    {
                        await Work();
                    }
                    catch (System.OperationCanceledException) // Noncompliant
                    {
                        return;
                    }
                }

                private System.Threading.Tasks.Task Work() => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantWhenOnlyOneBranchThrows() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public bool Process(bool propagate)
                {
                    try
                    {
                        Work();
                        return true;
                    }
                    catch (System.OperationCanceledException) // Noncompliant
                    {
                        if (propagate)
                        {
                            throw;
                        }
                        return false;
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantForOtherExceptions() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.InvalidOperationException)
                    {
                        System.Console.WriteLine("Failed");
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("CancellationShouldNotBeSuppressed.cs")
            .WithCodeFix<CS.CancellationShouldNotBeSuppressedCodeFix>()
            .WithCodeFixedPaths("CancellationShouldNotBeSuppressed.Fixed.cs")
            .VerifyCodeFix();

    // A throw inside a lambda exits the lambda, not the catch block, so it does not count as rethrowing.
    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantWhenOnlyALambdaThrows() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.OperationCanceledException) // Noncompliant {{Do not turn cancellation into success - let 'OperationCanceledException' propagate or rethrow it.}}
                    {
                        System.Action fail = () => throw new System.InvalidOperationException();
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    // The documented way to tell an HttpClient timeout apart from a caller's cancellation: the filter only lets the
    // exception through when nobody asked to stop, so handling it there hides no cancellation signal.
    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantForTimeoutDistinguishingFilter() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderService
            {
                public void Process(CancellationToken cancellationToken)
                {
                    try
                    {
                        Work(cancellationToken);
                    }
                    catch (System.Threading.Tasks.TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new System.TimeoutException();
                    }
                }

                private void Work(CancellationToken cancellationToken) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantForExplicitRequestedCancellationFilter() =>
        builder.AddSnippet(
            """
            public class Worker
            {
                public void Run(System.Threading.CancellationToken stoppingToken)
                {
                    try
                    {
                        Work(stoppingToken);
                    }
                    catch (System.OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        System.Console.WriteLine("Stopped");
                    }
                }

                private void Work(System.Threading.CancellationToken cancellationToken) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantAtBackgroundServiceBoundary() =>
        builder.AddSnippet(
            """
            namespace Microsoft.Extensions.Hosting
            {
                public abstract class BackgroundService
                {
                    protected abstract System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken);
                }
            }

            public class Worker : Microsoft.Extensions.Hosting.BackgroundService
            {
                protected override async System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(1000, stoppingToken);
                    }
                    catch (System.OperationCanceledException)
                    {
                        System.Console.WriteLine("Stopped");
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantInsideCancellationControlledLoop() =>
        builder.AddSnippet(
            """
            public class Worker
            {
                public void Run(System.Threading.CancellationToken stoppingToken)
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            Work(stoppingToken);
                        }
                        catch (System.OperationCanceledException)
                        {
                            System.Console.WriteLine("Stopped");
                        }
                    }
                }

                private void Work(System.Threading.CancellationToken cancellationToken) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantWhenDisjunctionCanAcceptCancellation() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderService
            {
                public void Process(CancellationToken cancellationToken, bool retryTimeouts)
                {
                    try
                    {
                        Work(cancellationToken);
                    }
                    catch (System.Threading.Tasks.TaskCanceledException) when (!cancellationToken.IsCancellationRequested || retryTimeouts) // Noncompliant
                    {
                        System.Console.WriteLine("Suppressed");
                    }
                }

                private void Work(CancellationToken cancellationToken) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantWhenEveryDisjunctRejectsCancellation() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderService
            {
                public void Process(CancellationToken cancellationToken, bool firstTimeout, bool secondTimeout)
                {
                    try
                    {
                        Work(cancellationToken);
                    }
                    catch (System.Threading.Tasks.TaskCanceledException) when (
                        (!cancellationToken.IsCancellationRequested && firstTimeout)
                        || (!cancellationToken.IsCancellationRequested && secondTimeout))
                    {
                        throw new System.TimeoutException();
                    }
                }

                private void Work(CancellationToken cancellationToken) { }
            }
            """)
            .VerifyNoIssues();

    // Any other filter leaves the cancellation signal suppressed just the same.
    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantForUnrelatedFilter() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderService
            {
                public void Process(CancellationToken cancellationToken, bool retrying)
                {
                    try
                    {
                        Work(cancellationToken);
                    }
                    catch (System.Threading.Tasks.TaskCanceledException) when (!retrying) // Noncompliant {{Do not turn cancellation into success - let 'TaskCanceledException' propagate or rethrow it.}}
                    {
                        System.Console.WriteLine("Cancelled");
                    }
                }

                private void Work(CancellationToken cancellationToken) { }
            }
            """)
            .Verify();
}
