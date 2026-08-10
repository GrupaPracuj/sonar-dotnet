using System;

namespace Tests.Diagnostics
{
    public class UsingShouldNotUseThrowingObjectInitializer
    {
        public void Noncompliant()
        {
            using var conn = new FakeDisposable(); // Fixed
            conn.Value = Compute();
        }

        public void NoncompliantWithMultipleMembers()
        {
            using var conn = new FakeDisposable(); // Fixed
            conn.Value = Compute();
            conn.Name = "literal";
        }

        public void Compliant()
        {
            using var conn = new FakeDisposable { Value = 42 };
        }

        public void InitOnlyHasNoFix()
        {
            using var conn = new InitOnlyDisposable { Value = Compute() }; // Fixed
        }

        private static int Compute() => 42;
    }

    public class FakeDisposable : IDisposable
    {
        public int Value { get; set; }
        public string Name { get; set; }
        public void Dispose() { }
    }

    public class InitOnlyDisposable : IDisposable
    {
        public int Value { get; init; }
        public void Dispose() { }
    }
}
