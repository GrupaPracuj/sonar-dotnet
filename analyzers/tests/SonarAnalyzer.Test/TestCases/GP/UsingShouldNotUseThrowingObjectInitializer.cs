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

        public void InitOnlyHasNoFix()
        {
            using var conn = new InitOnlyDisposable { Value = Compute() }; // Noncompliant
        }

        public void RequiredMemberHasNoFix()
        {
            using var conn = new RequiredDisposable { Value = Compute() }; // Noncompliant
        }

        // A required field has to be set in the initializer too, so it cannot be moved out either.
        public void RequiredFieldHasNoFix()
        {
            using var conn = new RequiredFieldDisposable { Value = Compute() }; // Noncompliant
        }

        // A nested collection initializer has no statement form, so there is nothing to rewrite it into.
        public void NestedInitializerHasNoFix()
        {
            using var conn = new NestedDisposable { Items = { Compute() } }; // Noncompliant
        }

        // An indexer element is not a member name, so it cannot be moved out either.
        public void IndexerElementHasNoFix()
        {
            using var conn = new IndexedDisposable { [Compute()] = Compute() }; // Noncompliant
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

    public class RequiredFieldDisposable : IDisposable
    {
        public required int Value;
        public void Dispose() { }
    }
}
