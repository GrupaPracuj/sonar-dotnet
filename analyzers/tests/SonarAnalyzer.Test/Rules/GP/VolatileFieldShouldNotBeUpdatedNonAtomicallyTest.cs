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
public class VolatileFieldShouldNotBeUpdatedNonAtomicallyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.VolatileFieldShouldNotBeUpdatedNonAtomically>();

    [TestMethod]
    public void VolatileFieldShouldNotBeUpdatedNonAtomically_NoncompliantForIncrement() =>
        builder.AddSnippet(
            """
            public class OrderProcessor
            {
                private volatile int _processed;

                public void Process()
                {
                    _processed++; // Noncompliant {{'_processed' is volatile, which does not make this update atomic - use Interlocked or a lock.}}
                    ++_processed; // Noncompliant {{'_processed' is volatile, which does not make this update atomic - use Interlocked or a lock.}}
                    _processed--; // Noncompliant {{'_processed' is volatile, which does not make this update atomic - use Interlocked or a lock.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void VolatileFieldShouldNotBeUpdatedNonAtomically_NoncompliantForCompoundAssignment() =>
        builder.AddSnippet(
            """
            public class OrderProcessor
            {
                private volatile int _processed;

                public void Process(int count)
                {
                    _processed += count; // Noncompliant {{'_processed' is volatile, which does not make this update atomic - use Interlocked or a lock.}}
                    _processed |= 1;     // Noncompliant {{'_processed' is volatile, which does not make this update atomic - use Interlocked or a lock.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void VolatileFieldShouldNotBeUpdatedNonAtomically_NoncompliantForReadWriteBack() =>
        builder.AddSnippet(
            """
            public class OrderProcessor
            {
                private volatile int _processed;

                public void Process()
                {
                    _processed = _processed + 1; // Noncompliant {{'_processed' is volatile, which does not make this update atomic - use Interlocked or a lock.}}
                }
            }
            """)
            .Verify();

    // A plain write of a value that does not depend on the current one is what volatile does guarantee.
    [TestMethod]
    public void VolatileFieldShouldNotBeUpdatedNonAtomically_CompliantForPlainAssignment() =>
        builder.AddSnippet(
            """
            public class OrderProcessor
            {
                private volatile bool _shouldStop;
                private volatile int _processed;

                public void Stop(int reset)
                {
                    _shouldStop = true;
                    _processed = reset;
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void VolatileFieldShouldNotBeUpdatedNonAtomically_ComparesFieldAndReceiver() =>
        builder.AddSnippet(
            """
            public class Source
            {
                public volatile int Value;
            }

            public class Target
            {
                public volatile int Value;

                public void Copy(Source source, Target other)
                {
                    Value = source.Value;
                    Value = other.Value;
                    other.Value = other.Value + 1; // Noncompliant {{'Value' is volatile, which does not make this update atomic - use Interlocked or a lock.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void VolatileFieldShouldNotBeUpdatedNonAtomically_CompliantForNonVolatileField() =>
        builder.AddSnippet(
            """
            public class OrderProcessor
            {
                private int _processed;

                public void Process() => _processed++;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void VolatileFieldShouldNotBeUpdatedNonAtomically_CompliantForUnstablePropertyReceiver() =>
        builder.AddSnippet(
            """
            public class Counter
            {
                public volatile int Value;
            }

            public class Counters
            {
                private Counter Current => new Counter();

                public void Increment() => Current.Value = Current.Value + 1;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void VolatileFieldShouldNotBeUpdatedNonAtomically_CompliantForInterlocked() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderProcessor
            {
                private int _processed;

                public void Process() => Interlocked.Increment(ref _processed);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void VolatileFieldShouldNotBeUpdatedNonAtomically_CompliantInsideLock() =>
        builder.AddSnippet(
            """
            public class OrderProcessor
            {
                private readonly object _gate = new object();
                private volatile int _processed;

                public void Process()
                {
                    lock (_gate)
                    {
                        _processed++;
                        _processed += 2;
                        _processed = _processed + 1;
                    }
                }
            }
            """)
            .VerifyNoIssues();
}
