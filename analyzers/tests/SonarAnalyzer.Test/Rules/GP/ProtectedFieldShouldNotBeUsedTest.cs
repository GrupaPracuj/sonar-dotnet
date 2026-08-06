using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ProtectedFieldShouldNotBeUsedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ProtectedFieldShouldNotBeUsed>().WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_NoncompliantForPlainProtectedField() =>
        builder.AddSnippet(
            """
            public class Order
            {
                protected int _value; // Noncompliant {{'_value' should not have protected accessibility - use a protected property instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_CompliantWhenReadonly() =>
        builder.AddSnippet(
            """
            public class Order
            {
                protected readonly int _value;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_CompliantWhenConst() =>
        builder.AddSnippet(
            """
            public class Order
            {
                protected const int Value = 1;
            }
            """)
            .VerifyNoIssues();

    // Static fields are a different, less contested case that this guideline does not cover.
    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_CompliantWhenStatic() =>
        builder.AddSnippet(
            """
            public class Order
            {
                protected static int _value;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_CompliantWhenStructLayout() =>
        builder.AddSnippet(
            """
            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public class Order
            {
                protected int _value;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_CompliantWhenSerializable() =>
        builder.AddSnippet(
            """
            [System.Serializable]
            public class Order
            {
                protected int _value;
            }
            """)
            .VerifyNoIssues();

    // A field that opts back out of serialization is treated the same as if the type were not [Serializable].
    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_NoncompliantWhenSerializableButFieldIsNonSerialized() =>
        builder.AddSnippet(
            """
            [System.Serializable]
            public class Order
            {
                [System.NonSerialized]
                protected int _value; // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_CompliantForPrivateField() =>
        builder.AddSnippet(
            """
            public class Order
            {
                private int _value;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_CompliantForProtectedInternalField() =>
        builder.AddSnippet(
            """
            public class Order
            {
                protected internal int _value;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ProtectedFieldShouldNotBeUsed_CompliantForPrivateProtectedField() =>
        builder.AddSnippet(
            """
            public class Order
            {
                private protected int _value;
            }
            """)
            .VerifyNoIssues();
}
