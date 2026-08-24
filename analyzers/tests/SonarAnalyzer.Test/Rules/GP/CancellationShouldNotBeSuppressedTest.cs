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
    public void CancellationShouldNotBeSuppressed_CompliantForLocallyOwnedTimeout() =>
        builder.AddSnippet(
            """
            public class Queue
            {
                public async System.Threading.Tasks.Task TryQueue()
                {
                    try
                    {
                        using var timeout = new System.Threading.CancellationTokenSource();
                        timeout.CancelAfter(System.TimeSpan.FromSeconds(10));
                        await System.Threading.Tasks.Task.Delay(1000, timeout.Token);
                    }
                    catch (System.OperationCanceledException)
                    {
                        System.Console.WriteLine("Queue timeout");
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantAtHostedServiceStopBoundary() =>
        builder.AddSnippet(
            """
            namespace Microsoft.Extensions.Hosting
            {
                public interface IHostedService
                {
                    System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken);
                    System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken);
                }
            }

            public class Worker : Microsoft.Extensions.Hosting.IHostedService
            {
                public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) =>
                    System.Threading.Tasks.Task.CompletedTask;

                public async System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken)
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(-1, cancellationToken);
                    }
                    catch (System.OperationCanceledException)
                    {
                        System.Console.WriteLine("Host stopped waiting");
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantForHostedPeriodicTimerHelper() =>
        builder.AddSnippet(
            """
            namespace Microsoft.Extensions.Hosting
            {
                public interface IHostedService
                {
                    System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken);
                    System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken);
                }
            }

            public class Worker : Microsoft.Extensions.Hosting.IHostedService
            {
                private readonly System.Threading.PeriodicTimer timer =
                    new System.Threading.PeriodicTimer(System.TimeSpan.FromSeconds(1));

                public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) =>
                    Run(cancellationToken);

                public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) =>
                    System.Threading.Tasks.Task.CompletedTask;

                private async System.Threading.Tasks.Task Run(System.Threading.CancellationToken cancellationToken)
                {
                    try
                    {
                        while (await timer.WaitForNextTickAsync(cancellationToken))
                        {
                            System.Console.WriteLine("Tick");
                        }
                    }
                    catch (System.OperationCanceledException)
                    {
                        System.Console.WriteLine("Timer stopped");
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
    public void CancellationShouldNotBeSuppressed_CompliantAroundCancellationControlledLoop() =>
        builder.AddSnippet(
            """
            public class WaitingSpinner
            {
                public async System.Threading.Tasks.Task ShowWaitingSpinnerAsync(System.Threading.CancellationToken ct)
                {
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            await System.Threading.Tasks.Task.Delay(100, ct);
                        }
                    }
                    catch (System.OperationCanceledException)
                    {
                        System.Console.WriteLine("Cancelled");
                    }
                    finally
                    {
                        System.Console.WriteLine("Cleaned up");
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantAtAsyncStreamConsumptionBoundary() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public class StreamHandler
            {
                public async Task HandleAsync(System.Collections.Generic.IAsyncEnumerable<int> events)
                {
                    try
                    {
                        await foreach (var item in events)
                        {
                            System.Console.WriteLine(item);
                        }
                    }
                    catch (System.OperationCanceledException)
                    {
                        System.Console.WriteLine("Stream stopped");
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantAtAsyncStreamWithCallerToken() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public class StreamHandler
            {
                public async Task HandleAsync(
                    System.Collections.Generic.IAsyncEnumerable<int> events,
                    System.Threading.CancellationToken cancellationToken)
                {
                    try
                    {
                        await foreach (var item in events.WithCancellation(cancellationToken))
                        {
                            System.Console.WriteLine(item);
                        }
                    }
                    catch (System.OperationCanceledException) // Noncompliant
                    {
                        System.Console.WriteLine("Stream stopped");
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantWhenAsyncStreamCatchReturnsSuccessData() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public class StreamHandler
            {
                public async Task<bool> HandleAsync(
                    System.Collections.Generic.IAsyncEnumerable<int> events,
                    System.Threading.CancellationToken cancellationToken)
                {
                    try
                    {
                        await foreach (var item in events.WithCancellation(cancellationToken))
                        {
                            System.Console.WriteLine(item);
                        }
                        return true;
                    }
                    catch (System.OperationCanceledException) // Noncompliant
                    {
                        return false;
                    }
                }
            }
            """)
            .Verify();

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
