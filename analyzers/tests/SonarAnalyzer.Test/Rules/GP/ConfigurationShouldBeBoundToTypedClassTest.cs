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
