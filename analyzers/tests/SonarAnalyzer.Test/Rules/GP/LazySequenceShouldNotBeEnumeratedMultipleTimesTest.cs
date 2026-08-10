using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class LazySequenceShouldNotBeEnumeratedMultipleTimesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.LazySequenceShouldNotBeEnumeratedMultipleTimes>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_NoncompliantForTwoForEachLoops() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M1(IEnumerable<int> source)
                {
                    foreach (var x in source) { }
                    foreach (var y in source) { } // Noncompliant {{'source' is an unmaterialized sequence and is enumerated more than once here - each enumeration re-runs the underlying query/iterator. Materialize it once with '.ToList()' if you need to use it multiple times.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_NoncompliantForForEachThenLinqTerminal() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public void M2(IEnumerable<int> source)
                {
                    var count = source.Count();
                    var list = source.ToList(); // Noncompliant {{'source' is an unmaterialized sequence and is enumerated more than once here - each enumeration re-runs the underlying query/iterator. Materialize it once with '.ToList()' if you need to use it multiple times.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_CompliantWhenEnumeratedOnce() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M3(IEnumerable<int> source)
                {
                    foreach (var x in source) { }
                }
            }
            """)
            .VerifyNoIssues();

    // List<T> also implements IEnumerable<T>, but its DECLARED type is List<T>, not the interface, so repeated enumeration is safe.
    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_CompliantForMaterializedCollectionType() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M4(List<int> source)
                {
                    foreach (var x in source) { }
                    foreach (var y in source) { }
                }
            }
            """)
            .VerifyNoIssues();

    // The declared type is IEnumerable<int> via 'var' - it is the static type that matters, not the source-text spelling.
    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_NoncompliantForVarInferredLazySequence() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public void M5()
                {
                    var source = Enumerable.Range(1, 10);
                    foreach (var x in source) { }
                    var total = source.Sum(); // Noncompliant {{'source' is an unmaterialized sequence and is enumerated more than once here - each enumeration re-runs the underlying query/iterator. Materialize it once with '.ToList()' if you need to use it multiple times.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_NoncompliantForIQueryable() =>
        builder.AddSnippet(
            """
            using System.Linq;

            public class C
            {
                public void M6(IQueryable<int> source)
                {
                    var any = source.Any();
                    var count = source.Count(); // Noncompliant {{'source' is an unmaterialized sequence and is enumerated more than once here - each enumeration re-runs the underlying query/iterator. Materialize it once with '.ToList()' if you need to use it multiple times.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_CompliantForConstructorAndLocalFunction() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public C(IEnumerable<int> source)
                {
                    foreach (var x in source) { }
                    var total = source.Sum(); // Noncompliant {{'source' is an unmaterialized sequence and is enumerated more than once here - each enumeration re-runs the underlying query/iterator. Materialize it once with '.ToList()' if you need to use it multiple times.}}
                }

                public void M(IEnumerable<int> source)
                {
                    void Local(IEnumerable<int> innerSource)
                    {
                        foreach (var x in innerSource) { }
                        var total = innerSource.Sum(); // Noncompliant {{'innerSource' is an unmaterialized sequence and is enumerated more than once here - each enumeration re-runs the underlying query/iterator. Materialize it once with '.ToList()' if you need to use it multiple times.}}
                    }

                    foreach (var x in source) { }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_CompliantWhenChainedCallsAreNotRootedAtTheVariable() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public void M(IEnumerable<int> source)
                {
                    // Only the first call in the chain (Where) is rooted at 'source' - ToList() is rooted at the result of Where(),
                    // so this is a single enumeration site for 'source'.
                    var list = source.Where(x => x > 0).ToList();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_CompliantForMultipleDeferredOperations() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public void M(IEnumerable<int> source)
                {
                    var positives = source.Where(x => x > 0);
                    var strings = source.Select(x => x.ToString());
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_CompliantForMutuallyExclusiveBranches() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M(IEnumerable<int> source, bool useFirst)
                {
                    if (useFirst)
                    {
                        foreach (var x in source) { }
                    }
                    else
                    {
                        foreach (var x in source) { }
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_NoncompliantAfterMutuallyExclusiveBranches() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public void M(IEnumerable<int> source, bool useFirst)
                {
                    if (useFirst)
                    {
                        foreach (var x in source) { }
                    }
                    else
                    {
                        foreach (var x in source) { }
                    }

                    var count = source.Count(); // Noncompliant
                }
            }
            """)
            .Verify();

    // Two sections of the same switch never both run, so neither enumeration follows the other.
    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_CompliantForMutuallyExclusiveSwitchSections() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public int M(IEnumerable<int> source, int mode)
                {
                    switch (mode)
                    {
                        case 1:
                            return source.Count();
                        case 2:
                            return source.Sum();
                        default:
                            return source.First();
                    }
                }
            }
            """)
            .VerifyNoIssues();

    // The same for the arms of a switch expression.
    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_CompliantForMutuallyExclusiveSwitchExpressionArms() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public int M(IEnumerable<int> source, int mode) =>
                    mode switch
                    {
                        1 => source.Count(),
                        2 => source.Sum(),
                        _ => source.First(),
                    };
            }
            """)
            .VerifyNoIssues();

    // The governing expression always runs, so it is not exclusive with any section.
    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_NoncompliantForSwitchOverTheSequenceItself() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public int M(IEnumerable<int> source)
                {
                    switch (source.Count())
                    {
                        case 0:
                            return 0;
                        default:
                            return source.Sum(); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    // Two enumerations inside one section still follow each other.
    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_NoncompliantWithinASingleSwitchSection() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public int M(IEnumerable<int> source, int mode)
                {
                    switch (mode)
                    {
                        case 1:
                            var count = source.Count();
                            return count + source.Sum(); // Noncompliant
                        default:
                            return 0;
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void LazySequenceShouldNotBeEnumeratedMultipleTimes_NoncompliantAcrossGotoCase() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Linq;

            public class C
            {
                public int M(IEnumerable<int> source, int mode)
                {
                    switch (mode)
                    {
                        case 0:
                            source.Any();
                            goto case 1;
                        case 1:
                            return source.Count(); // Noncompliant
                        default:
                            return 0;
                    }
                }
            }
            """)
            .Verify();
}
