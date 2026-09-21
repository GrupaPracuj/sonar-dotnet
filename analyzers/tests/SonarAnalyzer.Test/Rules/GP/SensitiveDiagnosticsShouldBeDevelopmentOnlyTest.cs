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
public class SensitiveDiagnosticsShouldBeDevelopmentOnlyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.SensitiveDiagnosticsShouldBeDevelopmentOnly>();

    private const string Stubs =
        """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContextOptionsBuilder
            {
                public DbContextOptionsBuilder EnableSensitiveDataLogging(bool sensitiveDataLoggingEnabled = true) => this;
                public DbContextOptionsBuilder EnableDetailedErrors(bool detailedErrorsEnabled = true) => this;
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }

            public static class DatabaseDeveloperPageExceptionFilterServiceExtensions
            {
                public static IServiceCollection AddDatabaseDeveloperPageExceptionFilter(this IServiceCollection services) => services;
            }
        }

        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostEnvironment { }

            public static class HostEnvironmentEnvExtensions
            {
                public static bool IsDevelopment(this IHostEnvironment hostEnvironment) => true;
            }
        }
        """;

    [TestMethod]
    public void SensitiveDiagnosticsShouldBeDevelopmentOnly_NoncompliantWhenEnabledUnconditionally() =>
        builder.AddSnippet(
            Stubs + """

            public static class Setup
            {
                public static void Configure(
                    Microsoft.EntityFrameworkCore.DbContextOptionsBuilder options,
                    Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    options.EnableSensitiveDataLogging(); // Noncompliant {{Enable 'EnableSensitiveDataLogging' only in a development environment.}}
                    options.EnableDetailedErrors(true); // Noncompliant {{Enable 'EnableDetailedErrors' only in a development environment.}}
                    Microsoft.Extensions.DependencyInjection.DatabaseDeveloperPageExceptionFilterServiceExtensions // Noncompliant {{Enable 'AddDatabaseDeveloperPageExceptionFilter' only in a development environment.}}
                        .AddDatabaseDeveloperPageExceptionFilter(services);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void SensitiveDiagnosticsShouldBeDevelopmentOnly_CompliantWithFrameworkDevelopmentCheck() =>
        builder.AddSnippet(
            Stubs + """

            public static class Setup
            {
                public static void Configure(
                    Microsoft.EntityFrameworkCore.DbContextOptionsBuilder options,
                    Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    Microsoft.Extensions.Hosting.IHostEnvironment environment)
                {
                    if (Microsoft.Extensions.Hosting.HostEnvironmentEnvExtensions.IsDevelopment(environment))
                    {
                        options.EnableSensitiveDataLogging();
                        options.EnableDetailedErrors();
                        Microsoft.Extensions.DependencyInjection.DatabaseDeveloperPageExceptionFilterServiceExtensions
                            .AddDatabaseDeveloperPageExceptionFilter(services);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void SensitiveDiagnosticsShouldBeDevelopmentOnly_CompliantForConfigurationLambdaInsideDevelopmentCheck() =>
        builder.AddSnippet(
            Stubs + """

            public static class DatabaseRegistration
            {
                public static void AddDatabase(
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> configure) =>
                    configure(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder());
            }

            public static class Setup
            {
                public static void Configure(Microsoft.Extensions.Hosting.IHostEnvironment environment)
                {
                    if (Microsoft.Extensions.Hosting.HostEnvironmentEnvExtensions.IsDevelopment(environment))
                    {
                        DatabaseRegistration.AddDatabase(
                            options => options.EnableSensitiveDataLogging());
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void SensitiveDiagnosticsShouldBeDevelopmentOnly_CompliantWhenExplicitlyDisabledOrRuntimeControlled() =>
        builder.AddSnippet(
            Stubs + """

            public static class Setup
            {
                public static void Configure(
                    Microsoft.EntityFrameworkCore.DbContextOptionsBuilder options,
                    bool enabled)
                {
                    options.EnableSensitiveDataLogging(false);
                    options.EnableDetailedErrors(enabled);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void SensitiveDiagnosticsShouldBeDevelopmentOnly_NoncompliantForUnrelatedIsDevelopmentMethod() =>
        builder.AddSnippet(
            Stubs + """

            public static class EnvironmentHelper
            {
                public static bool IsDevelopment() => true;
            }

            public static class Setup
            {
                public static void Configure(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder options)
                {
                    var ignored = EnvironmentHelper.IsDevelopment();
                    options.EnableSensitiveDataLogging(); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void SensitiveDiagnosticsShouldBeDevelopmentOnly_CompliantInsideStartupDevelopment() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class StartupDevelopment
            {
                public void Configure(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder options) =>
                    options.EnableSensitiveDataLogging();
            }
            """)
            .VerifyNoIssues();
}
