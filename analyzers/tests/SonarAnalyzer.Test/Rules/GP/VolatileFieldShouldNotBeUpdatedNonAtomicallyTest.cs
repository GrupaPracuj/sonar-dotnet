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
}
