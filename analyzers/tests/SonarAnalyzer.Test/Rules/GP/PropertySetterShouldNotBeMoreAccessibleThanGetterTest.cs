using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PropertySetterShouldNotBeMoreAccessibleThanGetterTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PropertySetterShouldNotBeMoreAccessibleThanGetter>().WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void PropertySetterShouldNotBeMoreAccessibleThanGetter_NoncompliantWhenSetterIsPublicAndGetterIsProtected() =>
        builder.AddSnippet(
            """
            public class Order
            {
                public string Foo { protected get; set; } // Noncompliant {{'Foo' has a setter that is more accessible than its getter - narrow the setter or widen the getter.}}
            }
            """)
            .Verify();

    // The normal, common, encouraged pattern - a narrower setter - must not fire.
    [TestMethod]
    public void PropertySetterShouldNotBeMoreAccessibleThanGetter_CompliantWhenSetterIsNarrower() =>
        builder.AddSnippet(
            """
            public class Order
            {
                public string Foo { get; protected set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertySetterShouldNotBeMoreAccessibleThanGetter_CompliantWhenBothAreEqual() =>
        builder.AddSnippet(
            """
            public class Order
            {
                public string Foo { get; set; }
            }
            """)
            .VerifyNoIssues();

    // Accessibility is a lattice, not a total order: Protected and Internal are mutually incomparable, so neither
    // counts as "wider" than the other - this must not be a false positive. This pairing cannot actually be
    // written as a compilable property (C# rejects an accessor modifier that is not a strict subset of the
    // property's own accessibility with CS0273), so the lattice guard is exercised directly instead.
    [TestMethod]
    public void PropertySetterShouldNotBeMoreAccessibleThanGetter_ProtectedAndInternalAreIncomparable()
    {
        CS.PropertySetterShouldNotBeMoreAccessibleThanGetter.IsStrictlyWiderThan(Accessibility.Protected, Accessibility.Internal).Should().BeFalse();
        CS.PropertySetterShouldNotBeMoreAccessibleThanGetter.IsStrictlyWiderThan(Accessibility.Internal, Accessibility.Protected).Should().BeFalse();
    }

    [DataTestMethod]
    [DataRow(Accessibility.Public, Accessibility.ProtectedOrInternal, true)]
    [DataRow(Accessibility.Public, Accessibility.Protected, true)]
    [DataRow(Accessibility.Public, Accessibility.Internal, true)]
    [DataRow(Accessibility.Public, Accessibility.ProtectedAndInternal, true)]
    [DataRow(Accessibility.Public, Accessibility.Private, true)]
    [DataRow(Accessibility.Public, Accessibility.Public, false)]
    [DataRow(Accessibility.ProtectedOrInternal, Accessibility.Protected, true)]
    [DataRow(Accessibility.ProtectedOrInternal, Accessibility.Internal, true)]
    [DataRow(Accessibility.ProtectedOrInternal, Accessibility.ProtectedAndInternal, true)]
    [DataRow(Accessibility.ProtectedOrInternal, Accessibility.Private, true)]
    [DataRow(Accessibility.Protected, Accessibility.ProtectedAndInternal, true)]
    [DataRow(Accessibility.Protected, Accessibility.Private, true)]
    [DataRow(Accessibility.Protected, Accessibility.Internal, false)]
    [DataRow(Accessibility.Internal, Accessibility.ProtectedAndInternal, true)]
    [DataRow(Accessibility.Internal, Accessibility.Private, true)]
    [DataRow(Accessibility.Internal, Accessibility.Protected, false)]
    [DataRow(Accessibility.ProtectedAndInternal, Accessibility.Private, true)]
    [DataRow(Accessibility.Private, Accessibility.Public, false)]
    public void PropertySetterShouldNotBeMoreAccessibleThanGetter_IsStrictlyWiderThanMatchesTheLattice(Accessibility wider, Accessibility narrower, bool expected) =>
        CS.PropertySetterShouldNotBeMoreAccessibleThanGetter.IsStrictlyWiderThan(wider, narrower).Should().Be(expected);

    // "protected internal" (ProtectedOrInternal) is unambiguously wider than plain "protected" - only one accessor
    // may carry an explicit modifier in C#, so the property's own declared accessibility supplies the other side.
    [TestMethod]
    public void PropertySetterShouldNotBeMoreAccessibleThanGetter_NoncompliantWhenSetterIsProtectedInternalAndGetterIsProtected() =>
        builder.AddSnippet(
            """
            public class Order
            {
                protected internal string Foo { protected get; set; } // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void PropertySetterShouldNotBeMoreAccessibleThanGetter_CompliantForSetOnlyProperty() =>
        builder.AddSnippet(
            """
            public class Order
            {
                public string Foo { set { } }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertySetterShouldNotBeMoreAccessibleThanGetter_NoncompliantForIndexer() =>
        builder.AddSnippet(
            """
            public class Order
            {
                public string this[int index] { protected get => null; set { } } // Noncompliant
            }
            """)
            .Verify();
}
