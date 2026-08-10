using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PropertyShouldNotReturnArrayTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PropertyShouldNotReturnArray>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void PropertyShouldNotReturnArray_NoncompliantForPublicGetOnlyProperty() =>
        builder.AddSnippet(
            """
            public class Book
            {
                public string[] Pages { get; } // Noncompliant {{'Pages' returns an array - callers can mutate it through this property. Return a read-only collection, or a method that returns a copy.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PropertyShouldNotReturnArray_NoncompliantForProtectedProperty() =>
        builder.AddSnippet(
            """
            public class Book
            {
                protected int[] Scores { get; set; } // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForPrivateProperty() =>
        builder.AddSnippet(
            """
            public class Book
            {
                private string[] Pages { get; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForPrivateGetter() =>
        builder.AddSnippet(
            """
            public class Book
            {
                public string[] Pages { private get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForReadOnlyCollectionProperty() =>
        builder.AddSnippet(
            """
            using System.Collections.ObjectModel;

            public class Book
            {
                public ReadOnlyCollection<string> Pages { get; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForAttributeType() =>
        builder.AddSnippet(
            """
            public class MyAttribute : System.Attribute
            {
                public string[] AllowedValues { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForMessageContractType() =>
        builder.AddSnippet(
            """
            public class UpdateOrderRequest
            {
                public int[] ItemIds { get; set; }
            }
            """)
            .VerifyNoIssues();

    // Only the override in Book is excluded - the abstract declaration in BookBase is still reported, since that is
    // the one site where the shape could actually be changed.
    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForOverride() =>
        builder.AddSnippet(
            """
            public abstract class BookBase
            {
                public abstract string[] Pages { get; } // Noncompliant
            }

            public class Book : BookBase
            {
                public override string[] Pages { get; }
            }
            """)
            .Verify();
}
