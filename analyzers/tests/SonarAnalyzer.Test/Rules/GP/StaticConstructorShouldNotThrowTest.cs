using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class StaticConstructorShouldNotThrowTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.StaticConstructorShouldNotThrow>().WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void StaticConstructorShouldNotThrow_NoncompliantForDirectThrow() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                static ConfigurationCache() // Noncompliant {{Static constructors should not throw - it permanently poisons 'ConfigurationCache' for the rest of the process.}}
                {
                    throw new System.InvalidOperationException();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void StaticConstructorShouldNotThrow_NoncompliantForThrowExpression() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                private static readonly string ConnectionString;

                static ConfigurationCache() // Noncompliant
                {
                    ConnectionString = System.Environment.GetEnvironmentVariable("CONNECTION_STRING") ?? throw new System.InvalidOperationException();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void StaticConstructorShouldNotThrow_CompliantWhenNoThrow() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                static ConfigurationCache()
                {
                    try
                    {
                        DoSomething();
                    }
                    catch
                    {
                    }
                }

                private static void DoSomething() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void StaticConstructorShouldNotThrow_CompliantWhenThrowIsCaught() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                static ConfigurationCache()
                {
                    try
                    {
                        throw new System.InvalidOperationException();
                    }
                    catch (System.InvalidOperationException)
                    {
                    }
                }
            }
            """)
            .VerifyNoIssues();

    // A throw inside a lambda assigned to a field runs later, if and when the delegate is invoked - not
    // synchronously as part of type initialization - so it does not poison the type.
    [TestMethod]
    public void StaticConstructorShouldNotThrow_CompliantWhenThrowOnlyInsideALambda() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                private static System.Action FailLater;

                static ConfigurationCache()
                {
                    FailLater = () => throw new System.InvalidOperationException();
                }
            }
            """)
            .VerifyNoIssues();

    // Same reasoning as the lambda case, for a local function that is declared but not called synchronously.
    [TestMethod]
    public void StaticConstructorShouldNotThrow_CompliantWhenThrowOnlyInsideALocalFunction() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                static ConfigurationCache()
                {
                    void FailLater()
                    {
                        throw new System.InvalidOperationException();
                    }
                }
            }
            """)
            .VerifyNoIssues();

    // Instance constructors are out of scope - the guideline this rule is based on explicitly allows them to throw.
    [TestMethod]
    public void StaticConstructorShouldNotThrow_CompliantForInstanceConstructor() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                public ConfigurationCache()
                {
                    throw new System.InvalidOperationException();
                }
            }
            """)
            .VerifyNoIssues();
}
