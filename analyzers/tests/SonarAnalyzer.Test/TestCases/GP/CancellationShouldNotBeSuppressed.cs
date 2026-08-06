using System;
using System.Threading.Tasks;

namespace Tests.Diagnostics
{
    public class CancellationShouldNotBeSuppressed
    {
        public void SwallowsOperationCanceled()
        {
            try
            {
                Work();
            }
            catch (OperationCanceledException) // Noncompliant {{Do not turn cancellation into success - let 'OperationCanceledException' propagate or rethrow it.}}
            {
                Console.WriteLine("Cancelled");
            }
        }

        public void SwallowsTaskCanceled()
        {
            try
            {
                Work();
            }
            catch (TaskCanceledException) // Noncompliant {{Do not turn cancellation into success - let 'TaskCanceledException' propagate or rethrow it.}}
            {
            }
        }

        public void Compliant()
        {
            try
            {
                Work();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Cancelled");
                throw;
            }
        }

        private void Work() { }
    }
}
