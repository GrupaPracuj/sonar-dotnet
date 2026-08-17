using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DefaultStructEqualityShouldNotBeUsedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DefaultStructEqualityShouldNotBeUsed>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        public struct PlainPoint
        {
            public int X, Y;
        }

        // Overrides Equals/GetHashCode and, because that alone does not make '==' valid on a struct, also overloads
        // the operators so they can be compared to begin with.
        public struct GoodPoint
        {
            public int X, Y;

            public override bool Equals(object obj) => obj is GoodPoint p && X == p.X && Y == p.Y;
            public override int GetHashCode() => (X, Y).GetHashCode();
            public static bool operator ==(GoodPoint a, GoodPoint b) => a.Equals(b);
            public static bool operator !=(GoodPoint a, GoodPoint b) => !a.Equals(b);
        }

        public readonly record struct RecordPoint(int X, int Y);

        // Implements IEquatable<T> without also overriding Equals(object)/GetHashCode - a common half-finished fix:
        // calls that resolve to Equals(PlainPointWithIEquatable) are fast and fine, but the type still falls back to
        // ValueType's slow, reflection-based comparison for anything that resolves to Equals(object) instead.
        public struct PlainPointWithIEquatable : System.IEquatable<PlainPointWithIEquatable>
        {
            public int X, Y;

            public bool Equals(PlainPointWithIEquatable other) => X == other.X && Y == other.Y;
        }

        public struct EqualsOnlyPoint
        {
            public int X, Y;

            public override bool Equals(object obj) => obj is EqualsOnlyPoint p && X == p.X && Y == p.Y;
        }
        """;

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForDynamicOperatorUsage() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    dynamic left = new PlainPoint();
                    var b1 = left == new PlainPoint();
                    var b2 = left != new PlainPoint();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForOperatorUsageWithCustomOperator() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    dynamic left = new GoodPoint();
                    var b = left == new GoodPoint();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForOperatorUsageOnRecordStruct() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var b = new RecordPoint(1, 2) == new RecordPoint(1, 2);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_NoncompliantForEqualsInvocation() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var result = new PlainPoint().Equals(new PlainPoint()); // Noncompliant {{'PlainPoint.Equals()' uses the slow, reflection-based default - override Equals/GetHashCode on 'PlainPoint' for a real fix, or avoid relying on this comparison.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForEqualsInvocationWithOverride() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var result = new GoodPoint().Equals(new GoodPoint());
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_EvaluatesEqualsAndGetHashCodeIndependently() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var equals = new EqualsOnlyPoint().Equals(new EqualsOnlyPoint());
                    var dictionary = new System.Collections.Generic.Dictionary<EqualsOnlyPoint, string>(); // Noncompliant {{'EqualsOnlyPoint' is used as a Dictionary/HashSet key but does not override Equals/GetHashCode - lookups will use slow, reflection-based comparison.}}
                }
            }
            """)
            .Verify();

    // Overload resolution picks the fast, typed IEquatable<T>.Equals(T) overload here (an exact match beats the
    // boxing conversion Equals(object) would need), so this specific call never touches ValueType.Equals and must
    // not be flagged - even though the type still has not fixed its Equals(object)/GetHashCode.
    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForEqualsInvocationResolvingToIEquatableOverload() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var result = new PlainPointWithIEquatable().Equals(new PlainPointWithIEquatable());
                }
            }
            """)
            .VerifyNoIssues();

    // Boxing the argument to 'object' forces overload resolution back onto the inherited ValueType.Equals(object)
    // even though the type also has a typed IEquatable<T> overload - this is the actual slow path.
    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_NoncompliantForEqualsInvocationForcedToObjectOverload() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    object boxed = new PlainPointWithIEquatable();
                    var result = new PlainPointWithIEquatable().Equals(boxed); // Noncompliant {{'PlainPointWithIEquatable.Equals()' uses the slow, reflection-based default - override Equals/GetHashCode on 'PlainPointWithIEquatable' for a real fix, or avoid relying on this comparison.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_NoncompliantForDictionaryKey() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var d = new System.Collections.Generic.Dictionary<PlainPoint, string>(); // Noncompliant {{'PlainPoint' is used as a Dictionary/HashSet key but does not override Equals/GetHashCode - lookups will use slow, reflection-based comparison.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_NoncompliantForBraceOnlyDictionaryCreation() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var d = new System.Collections.Generic.Dictionary<PlainPoint, string> { }; // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_NoncompliantForHashSetElement() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var h = new System.Collections.Generic.HashSet<PlainPoint>(); // Noncompliant {{'PlainPoint' is used as a Dictionary/HashSet key but does not override Equals/GetHashCode - lookups will use slow, reflection-based comparison.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForDictionaryKeyWithOverride() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var d = new System.Collections.Generic.Dictionary<GoodPoint, string>();
                    var h = new System.Collections.Generic.HashSet<GoodPoint>();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForExplicitComparer() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class PointComparer : System.Collections.Generic.IEqualityComparer<PlainPoint>
            {
                public bool Equals(PlainPoint x, PlainPoint y) => x.X == y.X && x.Y == y.Y;
                public int GetHashCode(PlainPoint value) => (value.X, value.Y).GetHashCode();
            }

            public class C
            {
                public void M()
                {
                    var comparer = new PointComparer();
                    var d = new System.Collections.Generic.Dictionary<PlainPoint, string>(comparer);
                    var h = new System.Collections.Generic.HashSet<PlainPoint>(comparer);
                }
            }
            """)
            .VerifyNoIssues();

    // Only the key/element type matters - a struct without overridden equality used as a Dictionary *value* is not
    // hashed or compared by the dictionary itself, so it is not reported.
    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForDictionaryValueType() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var d = new System.Collections.Generic.Dictionary<string, PlainPoint>();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForRecordStructDictionaryKey() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var d = new System.Collections.Generic.Dictionary<RecordPoint, string>();
                }
            }
            """)
            .VerifyNoIssues();

    // Enums are a different TypeKind entirely (never TypeKind.Struct) and always use the built-in, fast comparison.
    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForEnumDictionaryKey() =>
        builder.AddSnippet(
            """
            public enum Color { Red, Green, Blue }

            public class C
            {
                public void M()
                {
                    var d = new System.Collections.Generic.Dictionary<Color, string>();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DefaultStructEqualityShouldNotBeUsed_CompliantForListOfPlainStruct() =>
        builder.AddSnippet(
            Stubs + """

            public class C
            {
                public void M()
                {
                    var list = new System.Collections.Generic.List<PlainPoint>();
                }
            }
            """)
            .VerifyNoIssues();
}
