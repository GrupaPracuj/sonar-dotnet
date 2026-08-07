using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ExplicitThrowShouldNotBeUsedInExceptionFilterTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ExplicitThrowShouldNotBeUsedInExceptionFilter>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ExplicitThrowShouldNotBeUsedInExceptionFilter_Noncompliant() =>
        builder.AddSnippet(
            """
            public class Handler
            {
                public void Run(bool canHandle)
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception) when (canHandle ? true : throw new System.InvalidOperationException()) // Noncompliant {{Remove this throw from the exception filter; the CLR silently treats the filter as false when it throws.}}
                    {
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void ExplicitThrowShouldNotBeUsedInExceptionFilter_CompliantOutsideFilterAndInsideDeferredLambda() =>
        builder.AddSnippet(
            """
            public class Handler
            {
                public void Run(bool canHandle)
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception) when (Evaluate(() => throw new System.InvalidOperationException()))
                    {
                        throw new System.InvalidOperationException();
                    }
                }

                private bool Evaluate(System.Func<bool> condition) => false;
                private void Work() { }
            }
            """)
            .VerifyNoIssues();
}
