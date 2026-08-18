using System;
using System.Threading;
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
            catch (OperationCanceledException) // Fixed
            {
                Console.WriteLine("Cancelled");
                throw;
            }
        }

        public void SwallowsTaskCanceled(CancellationToken cancellationToken)
        {
            try
            {
                Work(cancellationToken);
            }
            catch (TaskCanceledException) // Fixed
            {
                throw;
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
        private void Work(CancellationToken cancellationToken) { }
    }
}
