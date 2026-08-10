using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class CorsPolicyShouldNotAllowNullOriginTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CorsPolicyShouldNotAllowNullOrigin>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Cors.Infrastructure
        {
            public class CorsPolicyBuilder
            {
                public CorsPolicyBuilder(params string[] origins) { }
                public CorsPolicyBuilder WithOrigins(params string[] origins) => this;
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IHeaderDictionary
            {
                string this[string key] { get; set; }
                void Add(string key, string value);
            }

            public static class HeaderDictionaryExtensions
            {
                public static void Append(this IHeaderDictionary headers, string key, string value) { }
            }
        }
        """;

    [TestMethod]
    public void CorsPolicyShouldNotAllowNullOrigin_NoncompliantBuilderConfiguration() =>
        builder.AddSnippet(
            Stubs + """

            public class Startup
            {
                public void Configure()
                {
                    var first = new Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder("null"); // Noncompliant
                    var second = new Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder()
                        .WithOrigins("https://trusted.example", "null"); // Noncompliant@-1
                    var third = new Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder()
                        .WithOrigins(new string[] { "https://trusted.example", "null" }); // Noncompliant@-1
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CorsPolicyShouldNotAllowNullOrigin_NoncompliantHeaderWrites() =>
        builder.AddSnippet(
            Stubs + """

            namespace App
            {
                using Microsoft.AspNetCore.Http;

                public class Middleware
                {
                    public void Apply(IHeaderDictionary headers)
                    {
                        headers["Access-Control-Allow-Origin"] = "null"; // Noncompliant
                        headers.Add("Access-Control-Allow-Origin", "null"); // Noncompliant
                        headers.Append("Access-Control-Allow-Origin", "null"); // Noncompliant
                        HeaderDictionaryExtensions.Append(headers, "Access-Control-Allow-Origin", "null"); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CorsPolicyShouldNotAllowNullOrigin_CompliantTrustedAndDynamicOrigins() =>
        builder.AddSnippet(
            Stubs + """

            public class Startup
            {
                public void Configure(
                    Microsoft.AspNetCore.Http.IHeaderDictionary headers,
                    string configuredOrigin)
                {
                    var policy = new Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder("https://trusted.example")
                        .WithOrigins(configuredOrigin);
                    headers["Access-Control-Allow-Origin"] = configuredOrigin;
                    headers.Add("Other-Header", "null");
                    Microsoft.AspNetCore.Http.HeaderDictionaryExtensions.Append(headers, "Other-Header", "null");
                    Microsoft.AspNetCore.Http.HeaderDictionaryExtensions.Append(headers, "Access-Control-Allow-Origin", configuredOrigin);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CorsPolicyShouldNotAllowNullOrigin_CompliantForLookalikeApis() =>
        builder.AddSnippet(
            Stubs + """

            namespace Custom
            {
                public class CorsPolicyBuilder
                {
                    public CorsPolicyBuilder(string origin) { }
                    public CorsPolicyBuilder WithOrigins(string origin) => this;
                }

                public static class HeaderDictionaryExtensions
                {
                    public static void Append(
                        Microsoft.AspNetCore.Http.IHeaderDictionary headers,
                        string key,
                        string value) { }
                }
            }

            public class Startup
            {
                public void Configure(Microsoft.AspNetCore.Http.IHeaderDictionary headers)
                {
                    new Custom.CorsPolicyBuilder("null").WithOrigins("null");
                    Custom.HeaderDictionaryExtensions.Append(headers, "Access-Control-Allow-Origin", "null");
                }
            }
            """)
            .VerifyNoIssues();
}
