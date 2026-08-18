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
public class OpenApiSecuritySchemeIdsShouldMatchTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.OpenApiSecuritySchemeIdsShouldMatch>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        using Microsoft.Extensions.DependencyInjection;

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }

            public static class SwaggerGenServiceCollectionExtensions
            {
                public static IServiceCollection AddSwaggerGen(
                    this IServiceCollection services,
                    System.Action<Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions> configure)
                {
                    configure(new Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions());
                    return services;
                }
            }

            public static class SwaggerGenOptionsExtensions
            {
                public static void AddSecurityDefinition(
                    this Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options,
                    string name,
                    Microsoft.OpenApi.OpenApiSecurityScheme securityScheme) { }

                public static void AddSecurityRequirement(
                    this Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options,
                    System.Func<object, Microsoft.OpenApi.OpenApiSecurityRequirement> requirement) { }
            }
        }

        namespace Swashbuckle.AspNetCore.SwaggerGen
        {
            public class SwaggerGenOptions { }
        }

        namespace Microsoft.OpenApi
        {
            public class OpenApiSecurityScheme { }
            public class OpenApiSecurityRequirement :
                System.Collections.Generic.Dictionary<OpenApiSecuritySchemeReference, object> { }

            public class OpenApiSecuritySchemeReference
            {
                public OpenApiSecuritySchemeReference(string referenceId, object hostDocument) { }
            }
        }

        """;

    private const string LegacyStubs =
        """
        namespace Microsoft.OpenApi.Models
        {
            public class OpenApiSecurityScheme
            {
                public OpenApiReference Reference { get; set; }
            }

            public class OpenApiSecurityRequirement :
                System.Collections.Generic.Dictionary<OpenApiSecurityScheme, string[]> { }

            public enum ReferenceType
            {
                Schema,
                SecurityScheme,
            }

            public class OpenApiReference
            {
                public string Id { get; set; }
                public ReferenceType Type { get; set; }
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class LegacySwaggerGenOptionsExtensions
            {
                public static void AddSecurityRequirement(
                    this Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options,
                    Microsoft.OpenApi.Models.OpenApiSecurityRequirement requirement) { }
            }
        }

        """;

    [TestMethod]
    public void OpenApiSecuritySchemeIdsShouldMatch_NoncompliantForNewReferenceCasingMismatch() =>
        builder.AddSnippet(
            Stubs + """
            public static class SwaggerSetup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSwaggerGen(options =>
                    {
                        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme());
                        options.AddSecurityRequirement(document =>
                            new Microsoft.OpenApi.OpenApiSecurityRequirement
                            {
                                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", document)] = null // Noncompliant {{Security scheme reference 'bearer' differs in casing from definition 'Bearer'.}}
                            });
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void OpenApiSecuritySchemeIdsShouldMatch_NoncompliantForLegacyReferenceCasingMismatch() =>
        builder.AddSnippet(
            Stubs + LegacyStubs + """
            public static class SwaggerSetup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSwaggerGen(options =>
                    {
                        options.AddSecurityDefinition("bearer", new Microsoft.OpenApi.OpenApiSecurityScheme());
                        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                        {
                            [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                            {
                                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                                {
                                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                    Id = "Bearer" // Noncompliant {{Security scheme reference 'Bearer' differs in casing from definition 'bearer'.}}
                                }
                            }] = System.Array.Empty<string>()
                        });
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void OpenApiSecuritySchemeIdsShouldMatch_CompliantForExactMatch() =>
        builder.AddSnippet(
            Stubs + """
            public static class SwaggerSetup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSwaggerGen(options =>
                    {
                        options.AddSecurityDefinition("bearer", new Microsoft.OpenApi.OpenApiSecurityScheme());
                        options.AddSecurityRequirement(document =>
                            new Microsoft.OpenApi.OpenApiSecurityRequirement
                            {
                                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", document)] = null
                            });
                    });
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void OpenApiSecuritySchemeIdsShouldMatch_NoncompliantForReorderedNamedArguments() =>
        builder.AddSnippet(
            Stubs + """
            public static class SwaggerSetup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSwaggerGen(options =>
                    {
                        options.AddSecurityDefinition(
                            securityScheme: new Microsoft.OpenApi.OpenApiSecurityScheme(),
                            name: "Bearer");
                        options.AddSecurityRequirement(document =>
                            new Microsoft.OpenApi.OpenApiSecurityRequirement
                            {
                                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference(
                                    hostDocument: document,
                                    referenceId: "bearer")] = null // Noncompliant {{Security scheme reference 'bearer' differs in casing from definition 'Bearer'.}}
                            });
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void OpenApiSecuritySchemeIdsShouldMatch_CompliantWhenAnExactDefinitionExists() =>
        builder.AddSnippet(
            Stubs + """
            public static class SwaggerSetup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSwaggerGen(options =>
                    {
                        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme());
                        options.AddSecurityDefinition("bearer", new Microsoft.OpenApi.OpenApiSecurityScheme());
                        options.AddSecurityRequirement(document =>
                            new Microsoft.OpenApi.OpenApiSecurityRequirement
                            {
                                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", document)] = null
                            });
                    });
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void OpenApiSecuritySchemeIdsShouldMatch_DoesNotCrossSwaggerScopes() =>
        builder.AddSnippet(
            Stubs + """
            public static class SwaggerSetup
            {
                public static void Configure(
                    Microsoft.Extensions.DependencyInjection.IServiceCollection first,
                    Microsoft.Extensions.DependencyInjection.IServiceCollection second)
                {
                    first.AddSwaggerGen(options =>
                    {
                        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme());
                    });
                    second.AddSwaggerGen(options =>
                    {
                        options.AddSecurityRequirement(document =>
                            new Microsoft.OpenApi.OpenApiSecurityRequirement
                            {
                                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", document)] = null
                            });
                    });
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void OpenApiSecuritySchemeIdsShouldMatch_IgnoresReferenceWithoutCaseInsensitiveDefinition() =>
        builder.AddSnippet(
            Stubs + """
            public static class SwaggerSetup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSwaggerGen(options =>
                    {
                        options.AddSecurityDefinition("oauth", new Microsoft.OpenApi.OpenApiSecurityScheme());
                        options.AddSecurityRequirement(document =>
                            new Microsoft.OpenApi.OpenApiSecurityRequirement
                            {
                                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", document)] = null
                            });
                    });
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void OpenApiSecuritySchemeIdsShouldMatch_IgnoresLegacySchemaReference() =>
        builder.AddSnippet(
            Stubs + LegacyStubs + """
            public static class SwaggerSetup
            {
                public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSwaggerGen(options =>
                    {
                        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme());
                        var reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.Schema,
                            Id = "bearer"
                        };
                    });
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void OpenApiSecuritySchemeIdsShouldMatch_IgnoresDynamicIdsAndLookalikeApis() =>
        builder.AddSnippet(
            Stubs + """
            public class LocalOptions
            {
                public void AddSecurityDefinition(string id, object value) { }
            }

            public static class SwaggerSetup
            {
                public static void Configure(
                    Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    string id)
                {
                    services.AddSwaggerGen(options =>
                    {
                        options.AddSecurityDefinition(id, new Microsoft.OpenApi.OpenApiSecurityScheme());
                        options.AddSecurityRequirement(document =>
                            new Microsoft.OpenApi.OpenApiSecurityRequirement
                            {
                                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", document)] = null
                            });
                    });

                    new LocalOptions().AddSecurityDefinition("Bearer", new object());
                }
            }
            """)
            .VerifyNoIssues();
}
