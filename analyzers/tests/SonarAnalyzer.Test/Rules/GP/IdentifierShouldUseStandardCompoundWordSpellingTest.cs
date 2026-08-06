using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class IdentifierShouldUseStandardCompoundWordSpellingTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.IdentifierShouldUseStandardCompoundWordSpelling>();

    [TestMethod]
    public void IdentifierShouldUseStandardCompoundWordSpelling_NoncompliantForMergedCompoundWord() =>
        builder.AddSnippet(
            """
            public class HttpClientOptions
            {
                public string EndPoint { get; set; } // Noncompliant {{Rename 'EndPoint' to 'Endpoint' - that is the standard spelling for this compound word.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void IdentifierShouldUseStandardCompoundWordSpelling_NoncompliantForWronglyMergedWord() =>
        builder.AddSnippet(
            """
            public class Document
            {
                public string Filename { get; set; } // Noncompliant {{Rename 'Filename' to 'FileName' - that is the standard spelling for this compound word.}}
            }
            """)
            .Verify();

    // OrderID splits into "Order" + "ID"; only the single-word ID -> Id entry applies (there is no split-table
    // entry for "OrderID" as a whole), so the suggested fix is "OrderId".
    [TestMethod]
    public void IdentifierShouldUseStandardCompoundWordSpelling_NoncompliantForIdSuffixOnProperty() =>
        builder.AddSnippet(
            """
            public class Order
            {
                public int OrderID { get; set; } // Noncompliant {{Rename 'OrderID' to 'OrderId' - that is the standard spelling for this compound word.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void IdentifierShouldUseStandardCompoundWordSpelling_NoncompliantForIdSuffixOnParameter() =>
        builder.AddSnippet(
            """
            public class OrderRepository
            {
                public void Load(int orderID) // Noncompliant {{Rename 'orderID' to 'orderId' - that is the standard spelling for this compound word.}}
                {
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void IdentifierShouldUseStandardCompoundWordSpelling_CompliantForStandardSpellings() =>
        builder.AddSnippet(
            """
            public class HttpClientOptions
            {
                public string Endpoint { get; set; }
                public string FileName { get; set; }
                public int OrderId { get; set; }
            }
            """)
            .VerifyNoIssues();

    // A method whose name is fixed by an override is not the author's free choice - the base signature dictates it.
    [TestMethod]
    public void IdentifierShouldUseStandardCompoundWordSpelling_CompliantForOverride() =>
        builder.AddSnippet(
            """
            public abstract class Base
            {
                public abstract void CallBack(); // Noncompliant {{Rename 'CallBack' to 'Callback' - that is the standard spelling for this compound word.}}
            }

            public class Derived : Base
            {
                public override void CallBack() { }
            }
            """)
            .Verify();

    // A method that implicitly implements an interface member by name+signature is likewise not free to rename on
    // its own - renaming it would silently stop implementing the interface.
    [TestMethod]
    public void IdentifierShouldUseStandardCompoundWordSpelling_CompliantForImplicitInterfaceImplementation() =>
        builder.AddSnippet(
            """
            public interface IHandler
            {
                void CallBack(); // Noncompliant {{Rename 'CallBack' to 'Callback' - that is the standard spelling for this compound word.}}
            }

            public class Handler : IHandler
            {
                public void CallBack() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void IdentifierShouldUseStandardCompoundWordSpelling_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("IdentifierShouldUseStandardCompoundWordSpelling.cs")
            .WithCodeFix<CS.IdentifierShouldUseStandardCompoundWordSpellingCodeFix>()
            .WithCodeFixedPaths("IdentifierShouldUseStandardCompoundWordSpelling.Fixed.cs")
            .VerifyCodeFix();
}
