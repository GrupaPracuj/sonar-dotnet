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
    public void StaticConstructorShouldNotThrow_NoncompliantForStaticFieldInitializerThrowExpression() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                private static readonly string ConnectionString =
                    System.Environment.GetEnvironmentVariable("CONNECTION_STRING")
                    ?? throw new System.InvalidOperationException(); // Noncompliant@-2 {{Static constructors should not throw - it permanently poisons 'ConfigurationCache' for the rest of the process.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void StaticConstructorShouldNotThrow_NoncompliantForStaticPropertyInitializerThrowExpression() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                public static string ConnectionString { get; } =
                    System.Environment.GetEnvironmentVariable("CONNECTION_STRING")
                    ?? throw new System.InvalidOperationException(); // Noncompliant@-2 {{Static constructors should not throw - it permanently poisons 'ConfigurationCache' for the rest of the process.}}
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

    [TestMethod]
    public void StaticConstructorShouldNotThrow_CompliantWhenRethrowIsCaughtByEnclosingCatchAll() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                static ConfigurationCache()
                {
                    try
                    {
                        try
                        {
                            Initialize();
                        }
                        catch (System.InvalidOperationException)
                        {
                            throw;
                        }
                    }
                    catch
                    {
                    }
                }

                private static void Initialize() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void StaticConstructorShouldNotThrow_CompliantWhenRethrowIsCaughtByEnclosingExceptionCatch() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                static ConfigurationCache()
                {
                    try
                    {
                        try
                        {
                            Initialize();
                        }
                        catch (System.InvalidOperationException)
                        {
                            throw;
                        }
                    }
                    catch (System.Exception)
                    {
                    }
                }

                private static void Initialize() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void StaticConstructorShouldNotThrow_NoncompliantWhenRethrowHasNoEnclosingCatchAll() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                static ConfigurationCache() // Noncompliant
                {
                    try
                    {
                        try
                        {
                            Initialize();
                        }
                        catch (System.InvalidOperationException)
                        {
                            throw;
                        }
                    }
                    catch (System.ArgumentException)
                    {
                    }
                }

                private static void Initialize() { }
            }
            """)
            .Verify();

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

    [TestMethod]
    public void StaticConstructorShouldNotThrow_CompliantWhenInitializerThrowIsInsideLambda() =>
        builder.AddSnippet(
            """
            public class ConfigurationCache
            {
                private static readonly System.Func<string> ReadLater =
                    () => throw new System.InvalidOperationException();
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
