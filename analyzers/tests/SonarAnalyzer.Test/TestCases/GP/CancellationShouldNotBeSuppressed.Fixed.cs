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
            catch (OperationCanceledException) // Fixed
            {
                Console.WriteLine("Cancelled");
                throw;
            }
        }

        public void SwallowsTaskCanceled()
        {
            try
            {
                Work();
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
    }
}
