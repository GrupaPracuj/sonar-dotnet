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
public class SensitiveValuesShouldNotBeUsedInExceptionMessagesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.SensitiveValuesShouldNotBeUsedInExceptionMessages>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void SensitiveValuesShouldNotBeUsedInExceptionMessages_NoncompliantInterpolatedValues() =>
        builder.AddSnippet(
            """
            public static class AccountService
            {
                public static void Validate(string email, string token, string username, string login)
                {
                    throw new System.InvalidOperationException($"Account {email} is invalid"); // Noncompliant {{Do not include 'email' in an exception message - use a non-sensitive identifier instead.}}
                    throw new System.ArgumentException($"Invalid token {token}", nameof(token)); // Noncompliant {{Do not include 'token' in an exception message - use a non-sensitive identifier instead.}}
                    throw new System.InvalidOperationException($"Unknown user {username}"); // Noncompliant {{Do not include 'username' in an exception message - use a non-sensitive identifier instead.}}
                    throw new System.InvalidOperationException($"Invalid login {login}"); // Noncompliant {{Do not include 'login' in an exception message - use a non-sensitive identifier instead.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void SensitiveValuesShouldNotBeUsedInExceptionMessages_NoncompliantTargetTypedCreation() =>
        builder.AddSnippet(
            """
            public static class AccountService
            {
                public static System.Exception Create(string token) =>
                    new($"Invalid token {token}"); // Noncompliant {{Do not include 'token' in an exception message - use a non-sensitive identifier instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void SensitiveValuesShouldNotBeUsedInExceptionMessages_NoncompliantReorderedStringFormatArguments() =>
        builder.AddSnippet(
            """
            public static class AccountService
            {
                public static void Validate(string token) =>
                    throw new System.InvalidOperationException(
                        string.Format(arg0: token, format: "Invalid token {0}")); // Noncompliant {{Do not include 'token' in an exception message - use a non-sensitive identifier instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void SensitiveValuesShouldNotBeUsedInExceptionMessages_NoncompliantConcatenationAndFormat() =>
        builder.AddSnippet(
            """
            public static class AccountService
            {
                public static void Validate(string pesel, string apiKey)
                {
                    throw new System.InvalidOperationException("Invalid PESEL: " + pesel); // Noncompliant {{Do not include 'pesel' in an exception message - use a non-sensitive identifier instead.}}
                    throw new System.InvalidOperationException(
                        string.Format("API key {0} is invalid", apiKey)); // Noncompliant {{Do not include 'apiKey' in an exception message - use a non-sensitive identifier instead.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void SensitiveValuesShouldNotBeUsedInExceptionMessages_UsesConstructorMessageParameter() =>
        builder.AddSnippet(
            """
            public sealed class DomainException : System.Exception
            {
                public DomainException(int errorCode, string message) : base(message) { }
            }

            public static class AccountService
            {
                public static void Validate(string phone) =>
                    throw new DomainException(
                        errorCode: 42,
                        message: $"Invalid phone {phone}"); // Noncompliant {{Do not include 'phone' in an exception message - use a non-sensitive identifier instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void SensitiveValuesShouldNotBeUsedInExceptionMessages_CompliantForIdentifiersLiteralsAndCancellationToken() =>
        builder.AddSnippet(
            """
            public static class AccountService
            {
                public static void Validate(
                    System.Guid userId,
                    System.Threading.CancellationToken cancellationToken,
                    System.Exception emailException)
                {
                    var first = new System.InvalidOperationException($"User {userId} is invalid");
                    var second = new System.InvalidOperationException("The account is invalid");
                    var third = new System.InvalidOperationException($"Operation {cancellationToken} was cancelled");
                    var fourth = new System.InvalidOperationException($"Mail operation failed: {emailException}");
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void SensitiveValuesShouldNotBeUsedInExceptionMessages_CompliantForNonExceptionConstructor() =>
        builder.AddSnippet(
            """
            public sealed class Notification
            {
                public Notification(string message) { }
            }

            public static class AccountService
            {
                public static Notification Create(string email) =>
                    new Notification($"Account {email} is invalid");
            }
            """)
            .VerifyNoIssues();
}
