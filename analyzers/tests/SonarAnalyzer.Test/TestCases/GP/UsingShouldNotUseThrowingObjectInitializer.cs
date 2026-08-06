using System;

namespace Tests.Diagnostics
{
    public class UsingShouldNotUseThrowingObjectInitializer
    {
        public void Noncompliant()
        {
            using var conn = new FakeDisposable { Value = Compute() }; // Noncompliant {{This 'using' constructs 'conn' via an object initializer - if a member assignment throws, the instance is never bound and 'Dispose' is never called. Assign the risky members in separate statements after construction.}}
        }

        public void NoncompliantWithMultipleMembers()
        {
            using var conn = new FakeDisposable { Value = Compute(), Name = "literal" }; // Noncompliant
        }

        public void Compliant()
        {
            using var conn = new FakeDisposable { Value = 42 };
        }

        private static int Compute() => 42;
    }

    public class FakeDisposable : IDisposable
    {
        public int Value { get; set; }
        public string Name { get; set; }
        public void Dispose() { }
    }
}
