using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class AbrakadabraWordTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.AbrakadabraWord>();

    [TestMethod]
    public void AbrakadabraWord_NoncompliantInIdentifier() =>
        builder.AddSnippet(
            """
            public class Spells
            {
                public string Abrakadabra { get; set; } // Noncompliant {{Remove the word 'abrakadabra' from the code.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void AbrakadabraWord_NoncompliantInStringLiteralAndComment() =>
        builder.AddSnippet(
            """
            public class Spells
            {
                // abrakadabra, and the bug is gone           // Noncompliant {{Remove the word 'abrakadabra' from the code.}}
                public string Cast() => "ABRAKADABRA";        // Noncompliant {{Remove the word 'abrakadabra' from the code.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void AbrakadabraWord_OneIssuePerLine() =>
        builder.AddSnippet(
            """
            public class Spells
            {
                public string Cast() => "abrakadabra abrakadabra"; // Noncompliant {{Remove the word 'abrakadabra' from the code.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void AbrakadabraWord_Compliant() =>
        builder.AddSnippet(
            """
            public class Spells
            {
                public string Cast() => "hocus pocus";
            }
            """)
            .VerifyNoIssues();
}
