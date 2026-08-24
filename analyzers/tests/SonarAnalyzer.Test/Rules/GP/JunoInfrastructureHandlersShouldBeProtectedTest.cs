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
public class JunoInfrastructureHandlersShouldBeProtectedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.JunoInfrastructureHandlersShouldBeProtected>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        using System;
        using GP.Juno.Hosting.AspNetCore.HostBuilding;
        using Microsoft.AspNetCore.Builder;

        namespace Microsoft.Extensions.Configuration
        {
            public interface IConfigurationRoot { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public interface IApplicationBuilder { }

            public static class AuthorizationAppBuilderExtensions
            {
                public static IApplicationBuilder UseAuthorization(this IApplicationBuilder app) => app;
            }
        }

        namespace GP.Juno.Configuration
        {
            public sealed class AppConfig { }
        }

        namespace GP.Juno.Hosting.AspNetCore.HostBuilding
        {
            public static class ApplicationBuilderJunoExtensions
            {
                public static IApplicationBuilder UseJuno(
                    this IApplicationBuilder app,
                    Action<GP.Juno.Configuration.AppConfig, IApplicationBuilder, Microsoft.Extensions.Configuration.IConfigurationRoot> configureJuno) => app;
            }

            public static class AppConfigDistributedConfigExtensions
            {
                public static GP.Juno.Configuration.AppConfig UseDistributedConfig(
                    this GP.Juno.Configuration.AppConfig juno,
                    IApplicationBuilder app,
                    Microsoft.Extensions.Configuration.IConfigurationRoot configuration) => juno;
            }
        }

        namespace GP.Juno.Web.Api
        {
            public static class AppConfigWebAppExtensions
            {
                public static GP.Juno.Configuration.AppConfig UseWebApp(
                    this GP.Juno.Configuration.AppConfig juno,
                    IApplicationBuilder app) => juno;
            }
        }
        """;

    [TestMethod]
    public void JunoInfrastructureHandlersShouldBeProtected_ReportsDistributedConfigBeforeAuthorization() =>
        builder.AddSnippet(
            Stubs + """

            public static class Startup
            {
                public static void Configure(
                    IApplicationBuilder app,
                    Microsoft.Extensions.Configuration.IConfigurationRoot configuration)
                {
                    app.UseJuno(ConfigureJuno); // Noncompliant {{Protect Juno infrastructure handlers before UseJuno; the later UseAuthorization middleware does not cover these branch handlers.}}
                    app.UseAuthorization();
                }

                private static void ConfigureJuno(
                    GP.Juno.Configuration.AppConfig juno,
                    IApplicationBuilder app,
                    Microsoft.Extensions.Configuration.IConfigurationRoot configuration) =>
                    juno.UseDistributedConfig(app, configuration);
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoInfrastructureHandlersShouldBeProtected_ReportsInlineWebConfiguration() =>
        builder.AddSnippet(
            Stubs + """

            public static class Startup
            {
                public static void Configure(
                    IApplicationBuilder app,
                    Microsoft.Extensions.Configuration.IConfigurationRoot configuration)
                {
                    app.UseJuno((juno, builder, config) => juno.UseDistributedConfig(builder, config)); // Noncompliant
                    app.UseAuthorization();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoInfrastructureHandlersShouldBeProtected_ReportsDirectUseWebApp() =>
        builder.AddSnippet(
            Stubs + """

            public static class Startup
            {
                public static void Configure(
                    IApplicationBuilder app,
                    Microsoft.Extensions.Configuration.IConfigurationRoot configuration)
                {
                    app.UseJuno((juno, builder, _) => GP.Juno.Web.Api.AppConfigWebAppExtensions.UseWebApp(juno, builder)); // Noncompliant
                    app.UseAuthorization();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoInfrastructureHandlersShouldBeProtected_NoWebHandlersOrNoLaterAuthorizationAreIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public static class Startup
            {
                public static void WithoutWeb(
                    IApplicationBuilder app,
                    Microsoft.Extensions.Configuration.IConfigurationRoot configuration)
                {
                    app.UseJuno((juno, _, _) => { });
                    app.UseAuthorization();
                }

                public static void WithoutAuthorization(
                    IApplicationBuilder app,
                    Microsoft.Extensions.Configuration.IConfigurationRoot configuration)
                {
                    app.UseJuno((juno, builder, config) => juno.UseDistributedConfig(builder, config));
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoInfrastructureHandlersShouldBeProtected_EarlierOrDifferentPipelineAuthorizationIsIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public static class Startup
            {
                public static void Configure(
                    IApplicationBuilder app,
                    IApplicationBuilder other,
                    Microsoft.Extensions.Configuration.IConfigurationRoot configuration)
                {
                    app.UseAuthorization();
                    app.UseJuno((juno, builder, config) => juno.UseDistributedConfig(builder, config));
                    other.UseAuthorization();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoInfrastructureHandlersShouldBeProtected_LookalikesAreIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public static class Lookalike
            {
                public static IApplicationBuilder UseJuno(
                    this IApplicationBuilder app,
                    Action configure) => app;
            }

            public static class Startup
            {
                public static void Configure(IApplicationBuilder app)
                {
                    Lookalike.UseJuno(app, () => { });
                    app.UseAuthorization();
                }
            }
            """)
            .VerifyNoIssues();
}
