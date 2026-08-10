using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class NonDerivedInternalTypesShouldBeSealedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.NonDerivedInternalTypesShouldBeSealed>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void NonDerivedInternalTypesShouldBeSealed_NoncompliantForInternalClassWithNoSubtype() =>
        builder.AddSnippet(
            """
            internal class Repository // Noncompliant {{'Repository' has no subtype in this assembly and should be sealed.}}
            {
                public void Save() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void NonDerivedInternalTypesShouldBeSealed_NoncompliantForImplicitlyInternalTopLevelClass() =>
        builder.AddSnippet(
            """
            class Repository // Noncompliant
            {
            }
            """)
            .Verify();

    [TestMethod]
    public void NonDerivedInternalTypesShouldBeSealed_NoncompliantForInternalRecord() =>
        builder.AddSnippet(
            """
            internal record Repository(int Id); // Noncompliant
            """)
            .Verify();

    [TestMethod]
    public void NonDerivedInternalTypesShouldBeSealed_CompliantWhenAlreadySealed() =>
        builder.AddSnippet(
            """
            internal sealed class Repository
            {
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void NonDerivedInternalTypesShouldBeSealed_CompliantForAbstractOrStatic() =>
        builder.AddSnippet(
            """
            internal abstract class Repository
            {
            }

            internal static class Helpers
            {
            }
            """)
            .VerifyNoIssues();

    // Repository has a subtype (SqlRepository) so it is excluded - but SqlRepository itself has none, and is
    // reported like any other leaf type.
    [TestMethod]
    public void NonDerivedInternalTypesShouldBeSealed_CompliantWhenSubtypeExistsInAssembly() =>
        builder.AddSnippet(
            """
            internal class Repository
            {
            }

            internal class SqlRepository : Repository // Noncompliant
            {
            }
            """)
            .Verify();

    [TestMethod]
    public void NonDerivedInternalTypesShouldBeSealed_CompliantForPublicClass() =>
        builder.AddSnippet(
            """
            public class Repository
            {
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void NonDerivedInternalTypesShouldBeSealed_CompliantForPartialClass() =>
        builder.AddSnippet(
            """
            internal partial class Repository
            {
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void NonDerivedInternalTypesShouldBeSealed_CompliantWhenAssemblyHasInternalsVisibleTo() =>
        builder.AddSnippet(
            """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Other.Assembly")]

            internal class Repository
            {
            }
            """)
            .VerifyNoIssues();
}
