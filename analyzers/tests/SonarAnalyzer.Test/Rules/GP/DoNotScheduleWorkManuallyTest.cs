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
public class DoNotScheduleWorkManuallyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotScheduleWorkManually>()
        .AddReferences(MetadataReferenceFacade.SystemComponentModelTypeConverter)
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void DoNotScheduleWorkManually_NoncompliantForThreadingTimer() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading;

            public class ReportSender
            {
                private readonly Timer _timer;

                public ReportSender() =>
                    _timer = new Timer(_ => Send(), null, TimeSpan.Zero, TimeSpan.FromHours(1)); // Noncompliant {{Schedule this work through Juno (ISchedulerFactory / IScheduleJobsRegistry) instead of 'Timer'.}}

                private void Send() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotScheduleWorkManually_NoncompliantForTimersTimer() =>
        builder.AddSnippet(
            """
            public class ReportSender
            {
                private readonly System.Timers.Timer _timer = new System.Timers.Timer(1000); // Noncompliant {{Schedule this work through Juno (ISchedulerFactory / IScheduleJobsRegistry) instead of 'Timer'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotScheduleWorkManually_CompliantForOneShotThreadingTimer() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading;

            public class TimeoutSignal
            {
                private readonly Timer _timer;

                public TimeoutSignal() =>
                    _timer = new Timer(_ => Signal(), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

                private void Signal() { }
            }

            public class UnsignedTimeoutSignal
            {
                private readonly Timer _timer;

                public UnsignedTimeoutSignal() =>
                    _timer = new Timer(_ => Signal(), null, 1000u, uint.MaxValue);

                private void Signal() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotScheduleWorkManually_CompliantForOneShotTimersTimer() =>
        builder.AddSnippet(
            """
            public class TimeoutSignal
            {
                private readonly System.Timers.Timer _timer =
                    new System.Timers.Timer(1000) { AutoReset = false };
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotScheduleWorkManually_CompliantWhenTimerPeriodIsNotKnown() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading;

            public class Poller
            {
                private readonly Timer _timer;

                public Poller(TimeSpan period) =>
                    _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, period);

                private void Poll() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotScheduleWorkManually_NoncompliantForHangfire() =>
        builder.AddSnippet(
            """
            namespace Hangfire
            {
                public static class RecurringJob
                {
                    public static void AddOrUpdate(string id, System.Action action, string cron) { }
                }
            }

            public class ReportSender
            {
                public void Register() =>
                    Hangfire.RecurringJob.AddOrUpdate("reports", Send, "0 * * * *"); // Noncompliant {{Schedule this work through Juno (ISchedulerFactory / IScheduleJobsRegistry) instead of 'RecurringJob'.}}

                private void Send() { }
            }
            """)
            .Verify();

    // Task.Delay in a loop is used for far too many legitimate things to mean "scheduled job" on its own.
    [TestMethod]
    public void DoNotScheduleWorkManually_CompliantForTaskDelayLoop() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading.Tasks;

            public class Poller
            {
                public async Task Poll()
                {
                    while (true)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }
            }
            """)
            .VerifyNoIssues();
}
