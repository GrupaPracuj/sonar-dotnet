namespace SonarAnalyzer.CSharp.Rules;

internal static class GpHttpCallHelper
{
    private static readonly HashSet<string> JunoHttpTargetTypes = new(StringComparer.Ordinal)
    {
        "GP.Juno.HttpApiClient.HttpSending.HttpSender",
        "GP.Juno.HttpClient.IHttpClient",
        "GP.Juno.HttpClient.HttpRequestProperties",
        "GP.Juno.Abstractions.HttpApiClient.HttpSending.HttpSender",
        "GP.Juno.Abstractions.HttpClient.IHttpClient",
        "GP.Juno.Abstractions.HttpClient.HttpRequestProperties"
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

        return method.IsExtensionMethod
               && method.Parameters.Length > 0
               && IsHttpTargetType(method.Parameters[0].Type);
    }

    private static bool IsHttpTargetType(ITypeSymbol type)
    {
        var typeDisplayName = type?.ToDisplayString() ?? string.Empty;
        return JunoHttpTargetTypes.Contains(typeDisplayName)
               || FrameworkHttpTargetTypes.Contains(typeDisplayName);
    }
}
