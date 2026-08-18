/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class CaughtExceptionShouldBePreservedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CaughtExceptionShouldBePreserved>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string ExceptionType =
        """
        public sealed class OrderException : System.Exception
        {
            public OrderException(string message) : base(message) { }
            public OrderException(string message, System.Exception innerException) : base(message, innerException) { }
            public OrderException(System.Exception innerException) : base(innerException.Message, innerException) { }
            public string Detail { get; set; }
        }

        """;

    [TestMethod]
    public void CaughtExceptionShouldBePreserved_NoncompliantForCopiedMessage() =>
        builder.AddSnippet(
            ExceptionType + """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception ex)
                    {
                        throw new OrderException(ex.Message); // Noncompliant {{Preserve 'ex' as the inner exception when wrapping it.}}
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CaughtExceptionShouldBePreserved_NoncompliantForInterpolationAndToString() =>
        builder.AddSnippet(
            ExceptionType + """
            public class OrderService
            {
                public void First()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.InvalidOperationException error)
                    {
                        throw new OrderException($"Order failed: {error.Message}"); // Noncompliant
                    }
                }

                public void Second()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception error)
                    {
                        throw new OrderException(error.ToString()); // Noncompliant
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CaughtExceptionShouldBePreserved_NoncompliantWhenOnlyInitializerPreservesDetails() =>
        builder.AddSnippet(
            ExceptionType + """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception ex)
                    {
                        throw new OrderException("Order failed") { Detail = ex.Message }; // Noncompliant
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CaughtExceptionShouldBePreserved_NoncompliantForImplicitObjectCreation() =>
        builder.AddSnippet(
            ExceptionType + """
            public class OrderService
            {
                public OrderException Create()
                {
                    try
                    {
                        Work();
                        return null;
                    }
                    catch (System.Exception ex)
                    {
                        throw new(ex.Message); // Noncompliant
                    }
                }

                private void Work() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void CaughtExceptionShouldBePreserved_CompliantWhenPassedAsInnerException() =>
        builder.AddSnippet(
            ExceptionType + """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception ex)
                    {
                        throw new OrderException(ex.Message, ex);
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CaughtExceptionShouldBePreserved_CompliantForCastsParenthesesAndNullableSuppression() =>
        builder.AddSnippet(
            ExceptionType + """
            #nullable enable

            public class OrderService
            {
                public void First()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception ex)
                    {
                        throw new OrderException(ex.Message, (System.Exception)(ex));
                    }
                }

                public void Second()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception ex)
                    {
                        throw new OrderException(ex!);
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CaughtExceptionShouldBePreserved_CompliantForRethrowAndIndependentReplacement() =>
        builder.AddSnippet(
            ExceptionType + """
            public class OrderService
            {
                public void Rethrow()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception ex)
                    {
                        System.Console.WriteLine(ex.Message);
                        throw;
                    }
                }

                public void Replace()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception ex)
                    {
                        System.Console.WriteLine(ex.Message);
                        throw new OrderException("Stable public message");
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CaughtExceptionShouldBePreserved_IgnoresNestedFunctionsAndCatches() =>
        builder.AddSnippet(
            ExceptionType + """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch (System.Exception ex)
                    {
                        System.Action later = () => throw new OrderException(ex.Message);

                        void FailLater()
                        {
                            throw new OrderException(ex.Message);
                        }

                        try
                        {
                            Work();
                        }
                        catch (System.Exception inner)
                        {
                            throw new OrderException(ex.Message, inner);
                        }
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CaughtExceptionShouldBePreserved_CompliantForUnnamedCatch() =>
        builder.AddSnippet(
            ExceptionType + """
            public class OrderService
            {
                public void Process()
                {
                    try
                    {
                        Work();
                    }
                    catch
                    {
                        throw new OrderException("Order failed");
                    }
                }

                private void Work() { }
            }
            """)
            .VerifyNoIssues();
}
