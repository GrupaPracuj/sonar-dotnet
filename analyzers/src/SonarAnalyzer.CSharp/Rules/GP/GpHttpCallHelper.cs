namespace SonarAnalyzer.CSharp.Rules;

internal static class GpHttpCallHelper
{
    // Namespaces verified against the GP.Juno submodule source: HttpSender/IHttpClient/HttpRequestProperties are
    // physically under the GP.Juno.Abstractions assembly but declare the shorter GP.Juno.* namespace below - there
    // is no GP.Juno.Abstractions.* variant of any of these types.
    private static readonly HashSet<string> JunoHttpTargetTypes = new(StringComparer.Ordinal)
    {
        "GP.Juno.HttpApiClient.HttpSending.HttpSender",
        "GP.Juno.HttpClient.IHttpClient",
        "GP.Juno.HttpClient.HttpRequestProperties",
        // The service name (e.g. "skidblandir") is passed to Service(this IHttpClientBuilder, string) - the entry
        // point of the fluent chain (HttpClientBuilderExtensions.Service -> HttpRequestProperties.AddPath -> ...
        // .GetJson<T>()) - not to any method on IHttpClient/HttpRequestProperties directly.
        "GP.Juno.HttpClient.IHttpClientBuilder"
    };

    private static readonly HashSet<string> FrameworkHttpTargetTypes = new(StringComparer.Ordinal)
    {
        "System.Net.Http.HttpClient",
        "System.Net.Http.HttpMessageInvoker"
    };

    internal static bool IsHttpCall(IMethodSymbol method)
    {
        if (IsHttpTargetType(method.ContainingType))
        {
            return true;
        }

        if (!method.IsExtensionMethod)
        {
            return false;
        }

        // For an extension method called via instance syntax (x.Method(...), the common case), the symbol from
        // GetSymbolInfo is already reduced: Parameters excludes the receiver, so it must be read from ReceiverType.
        // Parameters[0] only holds the receiver when the method is referenced in its unreduced/static form.
        return IsHttpTargetType(method.ReceiverType)
               || (method.Parameters.Length > 0 && IsHttpTargetType(method.Parameters[0].Type));
    }

    private static bool IsHttpTargetType(ITypeSymbol type)
    {
        var typeDisplayName = type?.ToDisplayString() ?? string.Empty;
        return JunoHttpTargetTypes.Contains(typeDisplayName)
               || FrameworkHttpTargetTypes.Contains(typeDisplayName);
    }
}
