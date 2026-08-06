using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class CancellationShouldNotBeSuppressedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CancellationShouldNotBeSuppressed>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantForSwallowedOperationCanceled() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.OperationCanceledException) // Noncompliant {{Do not turn cancellation into success - let 'OperationCanceledException' propagate or rethrow it.}}
                    {
                        System.Console.WriteLine("Cancelled");
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantForSwallowedTaskCanceled() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Threading.Tasks.TaskCanceledException) // Noncompliant {{Do not turn cancellation into success - let 'TaskCanceledException' propagate or rethrow it.}}
                    {
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantWhenRethrown() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.OperationCanceledException)
                    {
                        System.Console.WriteLine("Cancelled");
                        throw;
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    // Returning a value makes the cancellation visible to the caller rather than hiding it.
    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantWhenMappedToAResult() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public bool Process()
                {
                    try
                    {
                        Work();
                        return true;
                    }
                    catch (System.OperationCanceledException)
                    {
                        return false;
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    // The idiomatic worker loop: cancellation means leave the loop and shut down, which is reacting, not suppressing.
    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantWhenBreakingOutOfALoop() =>
        builder.AddSnippet(
            """
            public class Worker
            {
                public void Run()
                {
                    while (true)
                    {
                        try
                        {
                            Work();
                        }
                        catch (System.OperationCanceledException)
                        {
                            break;
                        }
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CompliantForOtherExceptions() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.InvalidOperationException)
                    {
                        System.Console.WriteLine("Failed");
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CancellationShouldNotBeSuppressed_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("CancellationShouldNotBeSuppressed.cs")
            .WithCodeFix<CS.CancellationShouldNotBeSuppressedCodeFix>()
            .WithCodeFixedPaths("CancellationShouldNotBeSuppressed.Fixed.cs")
            .VerifyCodeFix();

    // A throw inside a lambda exits the lambda, not the catch block, so it does not count as rethrowing.
    [TestMethod]
    public void CancellationShouldNotBeSuppressed_NoncompliantWhenOnlyALambdaThrows() =>
        builder.AddSnippet(
            """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.OperationCanceledException) // Noncompliant {{Do not turn cancellation into success - let 'OperationCanceledException' propagate or rethrow it.}}
                    {
                        System.Action fail = () => throw new System.InvalidOperationException();
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();
}
