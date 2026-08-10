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

        public void RequiredMemberHasNoFix()
        {
            using var conn = new RequiredDisposable { Value = Compute() }; // Fixed
        }

        // A nested collection initializer has no statement form, so there is nothing to rewrite it into.
        public void NestedInitializerHasNoFix()
        {
            using var conn = new NestedDisposable { Items = { Compute() } }; // Fixed
        }

        // An indexer element is not a member name, so it cannot be moved out either.
        public void IndexerElementHasNoFix()
        {
            using var conn = new IndexedDisposable { [Compute()] = Compute() }; // Fixed
        }

        private static int Compute() => 42;
    }

    public class FakeDisposable : IDisposable
    {
        public int Value { get; set; }
        public string Name { get; set; }
        public void Dispose() { }
    }

    public class NestedDisposable : IDisposable
    {
        public System.Collections.Generic.List<int> Items { get; } = new();
        public void Dispose() { }
    }

    public class IndexedDisposable : IDisposable
    {
        public int this[int index] { get => 0; set { } }
        public void Dispose() { }
    }

    public class InitOnlyDisposable : IDisposable
    {
        public int Value { get; init; }
        public void Dispose() { }
    }

    public class RequiredDisposable : IDisposable
    {
        public required int Value { get; set; }
        public void Dispose() { }
    }
}
