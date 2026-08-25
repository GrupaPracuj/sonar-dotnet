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
public class ConfigurationShouldBeBoundToTypedClassTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ConfigurationShouldBeBoundToTypedClass>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.Extensions.Configuration
        {
            public interface IConfiguration
            {
                string this[string key] { get; }
                IConfigurationSection GetSection(string key);
            }

            public interface IConfigurationSection : IConfiguration { }

            public static class ConfigurationBinder
            {
                public static T Get<T>(this IConfiguration configuration) => default(T);
                public static T GetValue<T>(this IConfiguration configuration, string key) => default(T);
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceProvider { }
            public interface IServiceCollection { }

            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddSingleton(
                    this IServiceCollection services,
                    System.Func<object, object> factory) => services;

                public static IServiceCollection AddSingleton<T>(
                    this IServiceCollection services,
                    System.Func<IServiceProvider, T> factory) => services;
            }

        }

        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostBuilder { }
            public sealed class HostBuilderContext
            {
                public Microsoft.Extensions.Configuration.IConfiguration Configuration { get; set; }
            }
            public static class HostingHostBuilderExtensions
            {
                public static IHostBuilder ConfigureServices(
                    this IHostBuilder builder,
                    System.Action<HostBuilderContext, Microsoft.Extensions.DependencyInjection.IServiceCollection> configure) => builder;
            }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public interface IApplicationBuilder { }

            public class WebApplicationBuilder
            {
                public Microsoft.Extensions.Configuration.IConfiguration Configuration { get; }
                public Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; }
            }
        }

        public class OrdersConfig
        {
            public string BaseUrl { get; set; }
        }
        """;

    [TestMethod]
    // Mirrors W3CLoggingInjectionExtensions from GP.Scrooge: one private helper reached from both halves of the
    // composition root. The IServiceCollection caller was already accepted; the IApplicationBuilder one was not, and
    // because every call site has to qualify, the helper was reported.
    public void ConfigurationShouldBeBoundToTypedClass_CompliantForHelperUsedFromPipelineSetup() =>
        builder.AddSnippet(
            Stubs + """

            public static class W3CLoggingInjectionExtensions
            {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddW3CLogging(
                    this Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    IsEnabled(configuration) ? services : services;

                public static void UseW3CLogging(
                    this Microsoft.AspNetCore.Builder.IApplicationBuilder app,
                    Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    if (!IsEnabled(configuration))
                    {
                        return;
                    }
                }

                private static bool IsEnabled(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    configuration.GetSection("w3CLogging")["enabled"] == "true";
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_NoncompliantForIndexer() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

                public string BaseUrl() => _configuration["Orders:BaseUrl"]; // Noncompliant {{Bind configuration to a typed class instead of reading it by key.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_NoncompliantForGetValue() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

                public int Timeout() =>
                    Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<int>(_configuration, "Orders:Timeout"); // Noncompliant {{Bind configuration to a typed class instead of reading it by key.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_NoncompliantForSectionIndexer() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

                public string BaseUrl() => _configuration.GetSection("orders")["BaseUrl"]; // Noncompliant {{Bind configuration to a typed class instead of reading it by key.}}
            }
            """)
            .Verify();

    // GetSection(...).Get<T>() is the pattern the rule steers towards - it is how Juno's own examples bind config.
    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantForTypedBinding() =>
        builder.AddSnippet(
            Stubs + """

            public class Startup
            {
                public OrdersConfig Bind(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    Microsoft.Extensions.Configuration.ConfigurationBinder.Get<OrdersConfig>(configuration.GetSection("orders"));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantInServiceRegistrationMethod() =>
        builder.AddSnippet(
            Stubs + """

            public static class EfCoreModule
            {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddEfCore(
                    this Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    var connectionString = configuration["adoConnections:defaultConnection:connectionString"]
                        ?? throw new System.InvalidOperationException("Missing connection string.");
                    return services;
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantInTopLevelBootstrapAndLocalSetupHelper() =>
        new VerifierBuilder<CS.ConfigurationShouldBeBoundToTypedClass>()
            .WithOptions(LanguageOptions.CSharpLatest)
            .WithTopLevelStatements()
            .AddSnippet(
            """
            var builder = new Microsoft.AspNetCore.Builder.WebApplicationBuilder();
            var connectionString = builder.Configuration["adoConnections:defaultConnection:connectionString"];
            var tracingEndpoint = ReadTracingEndpoint(builder.Configuration);

            string ReadTracingEndpoint(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                configuration["tracing:endpoint"];
            """ + Stubs)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantInRegistrationLambda() =>
        builder.AddSnippet(
            Stubs + """

            public static class DependencyRegistration
            {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddDependency(
                    this Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
                        services,
                        _ => configuration["orders:baseUrl"]);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantInWebApplicationBuilderRegistrationMethod() =>
        builder.AddSnippet(
            Stubs + """

            public static class ApplicationSetup
            {
                public static Microsoft.AspNetCore.Builder.WebApplicationBuilder SetupApplication(
                    this Microsoft.AspNetCore.Builder.WebApplicationBuilder builder)
                {
                    var keysDirectory = builder.Configuration["dataProtection:keysDirectory"];
                    var expiration = Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<int>(
                        builder.Configuration,
                        "session:snapshotExpirationMinutes");
                    _ = builder.Services;
                    return builder;
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantInPrivateSetupHelpersUsedOnlyFromCompositionRoot() =>
        builder.AddSnippet(
            Stubs + """

            public static class OpenTelemetrySetup
            {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddTracing(
                    this Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    ConfigureW3C(configuration);
                    ConfigureTracing(configuration);
                    return services;
                }

                private static void ConfigureW3C(Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    _ = configuration["w3c:traceContext"];
                }

                private static void ConfigureTracing(Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    _ = ReadServiceName(configuration);
                }

                private static string ReadServiceName(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    configuration["tracing:serviceName"];
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantInExtractedServiceRegistrationLambda() =>
        builder.AddSnippet(
            Stubs + """

            public static class ServiceSetup
            {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddOrders(
                    this Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    System.Func<object, object> factory =
                        _ => configuration["orders:baseUrl"];
                    return Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
                        services,
                        factory);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantInHostBuilderConfigureServices() =>
        builder.AddSnippet(
            Stubs + """

            namespace App
            {
                using Microsoft.Extensions.Hosting;

                public static class Program
                {
                    public static IHostBuilder Configure(IHostBuilder builder) =>
                        builder.ConfigureServices(
                        (context, services) =>
                        {
                            _ = Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<string>(
                                context.Configuration,
                                "github:personalAccessToken");
                        });
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantInSingletonFactoryUsedFromRegistration() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class SolrExportConfiguration
            {
                public string DiscoveryServiceName { get; set; }
            }

            public static class SolrDependencyInjectionExtensions
            {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddSolr(
                    this Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
                    Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<
                        SolrExportConfiguration>(services, GetSolrExportConfiguration);

                private static SolrExportConfiguration GetSolrExportConfiguration(
                    Microsoft.Extensions.DependencyInjection.IServiceProvider services)
                {
                    Microsoft.Extensions.Configuration.IConfiguration configuration = null;
                    return new SolrExportConfiguration
                    {
                        DiscoveryServiceName =
                            Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<string>(
                                configuration.GetSection("solrExport"),
                                "discoveryServiceName")
                    };
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantInsideJunoInfrastructure() =>
        builder.AddSnippet(
            Stubs + """

            namespace GP.Juno.Tracing.DistributedConfig
            {
                public static class TracingConfigurationExtensions
                {
                    public static bool IsTracerEnabled(
                        this Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                        Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<bool>(
                            configuration,
                            "tracer:enabled");
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_NoncompliantInConfigureServicesLookalike() =>
        builder.AddSnippet(
            Stubs + """

            public static class CustomRegistration
            {
                public static void ConfigureServices(
                    System.Action<Microsoft.Extensions.DependencyInjection.IServiceCollection> configure) { }
            }

            public static class Program
            {
                public static void Configure(
                    Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    CustomRegistration.ConfigureServices(
                        services =>
                        {
                            _ = configuration["runtime:value"]; // Noncompliant
                        });
            }
            """)
            .Verify();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_NoncompliantInWebApplicationBuilderRuntimeHelper() =>
        builder.AddSnippet(
            Stubs + """

            public static class RuntimeSettings
            {
                public static string ReadSetting(
                    this Microsoft.AspNetCore.Builder.WebApplicationBuilder builder) =>
                    builder.Configuration["runtime:value"]; // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_NoncompliantInPrivateHelperUsedFromRuntimeCode() =>
        builder.AddSnippet(
            Stubs + """

            public class Repository
            {
                private readonly Microsoft.Extensions.Configuration.IConfiguration configuration;

                public Repository(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    this.configuration = configuration;

                public string ConnectionString() => ReadConnectionString();

                private string ReadConnectionString() =>
                    configuration["adoConnections:defaultConnection:connectionString"]; // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_NoncompliantInRuntimeServiceDespiteFailFast() =>
        builder.AddSnippet(
            Stubs + """

            public class Repository
            {
                private readonly Microsoft.Extensions.Configuration.IConfiguration configuration;

                public Repository(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    this.configuration = configuration;

                public string ConnectionString =>
                    configuration["adoConnections:defaultConnection:connectionString"] // Noncompliant
                    ?? throw new System.InvalidOperationException("Missing connection string.");
            }
            """)
            .Verify();

    [TestMethod]
    public void ConfigurationShouldBeBoundToTypedClass_CompliantForUnrelatedIndexer() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly System.Collections.Generic.IDictionary<string, string> _values;

                public string Get(string key) => _values[key];
            }
            """)
            .VerifyNoIssues();
}
