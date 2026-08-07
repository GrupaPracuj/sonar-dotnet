namespace SonarAnalyzer.CSharp.Rules;

internal static class GpAssemblyNames
{
    internal static bool Matches(string assemblyName, string configuredName) =>
        assemblyName.Equals(configuredName, StringComparison.OrdinalIgnoreCase)
        || assemblyName.EndsWith("." + configuredName, StringComparison.OrdinalIgnoreCase);
}
