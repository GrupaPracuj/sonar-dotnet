using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotLockAcrossProcessesManuallyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotLockAcrossProcessesManually>();

    [TestMethod]
    public void DoNotLockAcrossProcessesManually_NoncompliantForNamedMutex() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderImport
            {
                public void Import()
                {
                    var mutex = new Mutex(false, "Global\\order-import"); // Noncompliant {{Use Juno's ILockableFactory instead of 'Mutex' for locking across processes.}}
                    mutex.WaitOne();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotLockAcrossProcessesManually_NoncompliantForNamedSemaphore() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderImport
            {
                private readonly Semaphore _semaphore = new Semaphore(1, 1, "Global\\order-import"); // Noncompliant {{Use Juno's ILockableFactory instead of 'Semaphore' for locking across processes.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotLockAcrossProcessesManually_CompliantForNullOrEmptyName() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderImport
            {
                private readonly Mutex _nullMutex = new Mutex(false, null);
                private readonly Mutex _emptyMutex = new Mutex(false, "");
                private readonly Semaphore _nullSemaphore = new Semaphore(1, 1, null);
                private readonly Semaphore _emptySemaphore = new Semaphore(1, 1, "");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotLockAcrossProcessesManually_CompliantForNonconstantName() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderImport
            {
                public Mutex Create(string name) => new Mutex(false, name);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotLockAcrossProcessesManually_NoncompliantForConsulLock() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            namespace Consul
            {
                public interface IConsulClient
                {
                    Task AcquireLock(string key);
                }
            }

            public class OrderImport
            {
                private readonly Consul.IConsulClient _consul;

                public Task Import() => _consul.AcquireLock("order-import"); // Noncompliant {{Use Juno's ILockableFactory instead of 'IConsulClient' for locking across processes.}}
            }
            """)
            .Verify();

    // In-process synchronization has a legitimate answer and is deliberately not reported.
    [TestMethod]
    public void DoNotLockAcrossProcessesManually_CompliantForInProcessSynchronization() =>
        builder.AddSnippet(
            """
            using System.Threading;

            public class OrderImport
            {
                private readonly object _gate = new object();
                private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);
                private readonly Mutex _unnamed = new Mutex();

                public void Import()
                {
                    lock (_gate)
                    {
                        _semaphore.Wait();
                    }
                }
            }
            """)
            .VerifyNoIssues();
}
