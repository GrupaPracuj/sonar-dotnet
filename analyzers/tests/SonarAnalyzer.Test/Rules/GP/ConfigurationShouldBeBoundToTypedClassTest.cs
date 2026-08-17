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
            public interface IServiceCollection { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
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
