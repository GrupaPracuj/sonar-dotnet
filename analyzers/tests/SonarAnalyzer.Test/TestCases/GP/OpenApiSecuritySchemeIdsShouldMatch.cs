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

        public static void AddSecurityRequirement(
            this Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options,
            Microsoft.OpenApi.Models.OpenApiSecurityRequirement requirement) { }
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
                    [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", document)] = null // Noncompliant
                });

            options.AddSecurityDefinition("oauth", new Microsoft.OpenApi.OpenApiSecurityScheme());
            options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
            {
                [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "OAuth" // Noncompliant
                    }
                }] = System.Array.Empty<string>()
            });
        });
    }
}
