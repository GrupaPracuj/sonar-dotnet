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
public class StaticClockShouldNotBypassInjectedTimeProviderTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.StaticClockShouldNotBypassInjectedTimeProvider>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void StaticClockShouldNotBypassInjectedTimeProvider_ReportsStaticClocks() =>
        builder.AddSnippet(
            """
            public sealed class ReservationConsumer
            {
                private readonly System.TimeProvider timeProvider;

                public ReservationConsumer(System.TimeProvider timeProvider) =>
                    this.timeProvider = timeProvider;

                public void Consume()
                {
                    _ = System.TimeProvider.System; // Noncompliant {{Use the injected TimeProvider instead of the static clock 'TimeProvider.System'.}}
                    _ = System.DateTime.UtcNow; // Noncompliant {{Use the injected TimeProvider instead of the static clock 'DateTime.UtcNow'.}}
                    _ = System.DateTime.Now; // Noncompliant
                    _ = System.DateTime.Today; // Noncompliant
                    _ = System.DateTimeOffset.UtcNow; // Noncompliant
                    _ = System.DateTimeOffset.Now; // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void StaticClockShouldNotBypassInjectedTimeProvider_ReportsWithPrimaryConstructor() =>
        builder.AddSnippet(
            """
            public sealed class ReservationConsumer(System.TimeProvider timeProvider)
            {
                public System.DateTimeOffset Consume() =>
                    System.DateTimeOffset.UtcNow; // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void StaticClockShouldNotBypassInjectedTimeProvider_AllowsStaticClockWithoutAvailableProvider() =>
        builder.AddSnippet(
            """
            public sealed class ReservationConsumer
            {
                public System.DateTimeOffset Consume() => System.DateTimeOffset.UtcNow;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void StaticClockShouldNotBypassInjectedTimeProvider_AllowsSystemProviderAsDefaultDependency() =>
        builder.AddSnippet(
            """
            public sealed class ReservationConsumer
            {
                private readonly System.TimeProvider timeProvider = System.TimeProvider.System;

                public ReservationConsumer() { }

                public ReservationConsumer(System.TimeProvider timeProvider) =>
                    this.timeProvider = timeProvider ?? System.TimeProvider.System;

                public System.DateTimeOffset Consume() => timeProvider.GetUtcNow();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void StaticClockShouldNotBypassInjectedTimeProvider_AllowsClockImplementation() =>
        builder.AddSnippet(
            """
            public sealed class AppTimeProvider
            {
                private readonly System.TimeProvider fallback;

                public AppTimeProvider(System.TimeProvider fallback) => this.fallback = fallback;

                public System.DateTimeOffset GetUtcNow() => System.DateTimeOffset.UtcNow;
            }

            public sealed class SystemClock
            {
                private readonly System.TimeProvider fallback;

                public SystemClock(System.TimeProvider fallback) => this.fallback = fallback;

                public System.DateTimeOffset GetUtcNow() => System.DateTimeOffset.UtcNow;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void StaticClockShouldNotBypassInjectedTimeProvider_AllowsInjectedProviderUsage() =>
        builder.AddSnippet(
            """
            public sealed class ReservationConsumer
            {
                private readonly System.TimeProvider timeProvider;

                public ReservationConsumer(System.TimeProvider timeProvider) =>
                    this.timeProvider = timeProvider;

                public System.DateTimeOffset Consume() => timeProvider.GetUtcNow();
            }
            """)
            .VerifyNoIssues();
}
