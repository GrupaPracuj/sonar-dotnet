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

    private static readonly HashSet<string> FrameworkRequestMethods = new(StringComparer.Ordinal)
    {
        "DeleteAsync", "GetAsync", "GetByteArrayAsync", "GetStreamAsync", "GetStringAsync", "PatchAsync",
        "PostAsync", "PutAsync", "Send", "SendAsync",
    };

    private static readonly HashSet<string> FrameworkJsonRequestMethods = new(StringComparer.Ordinal)
    {
        "DeleteFromJsonAsync", "GetFromJsonAsync", "PatchAsJsonAsync", "PostAsJsonAsync", "PutAsJsonAsync",
    };

    private static readonly HashSet<string> JunoRequestMethods = new(StringComparer.Ordinal)
    {
        "Delete", "DeleteJson", "DeleteString", "Download", "Get", "GetBytes", "GetFile", "GetJson", "GetStream",
        "GetString", "Head", "Options", "Patch", "PatchJson", "PatchString", "Post", "PostFormMultipart",
        "PostFormUrlEncoded", "PostJson", "PostMultipart", "PostString", "Put", "PutFormUrlEncoded", "PutJson",
        "PutString", "Send", "SendJson", "SendString",
    };

    internal static bool IsHttpCall(IMethodSymbol method)
    {
        if (FrameworkHttpTargetTypes.Contains(method.ContainingType?.ToDisplayString())
            && FrameworkRequestMethods.Contains(method.Name))
        {
            return true;
        }

        if (method.ContainingType?.ToDisplayString() == "System.Net.Http.Json.HttpClientJsonExtensions"
            && FrameworkJsonRequestMethods.Contains(method.Name)
            && ReceiverType(method)?.ToDisplayString() == "System.Net.Http.HttpClient")
        {
            return true;
        }

        if (!method.IsExtensionMethod && JunoHttpTargetTypes.Contains(method.ContainingType?.ToDisplayString()))
        {
            return JunoRequestMethods.Contains(method.Name);
        }

        return method.IsExtensionMethod
               && JunoRequestMethods.Contains(method.Name)
               && JunoHttpTargetTypes.Contains(ReceiverType(method)?.ToDisplayString());
    }

    private static ITypeSymbol ReceiverType(IMethodSymbol method) =>
        method.ReceiverType ?? method.Parameters.FirstOrDefault()?.Type;
}
