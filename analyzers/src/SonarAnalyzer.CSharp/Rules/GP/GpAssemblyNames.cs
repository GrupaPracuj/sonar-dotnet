/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

internal static class GpAssemblyNames
{
    internal const string DefaultContractAssemblyNames = "*Contracts";

    internal static bool MatchesContractAssembly(string assemblyName) =>
        Matches(assemblyName, DefaultContractAssemblyNames);

    internal static bool Matches(string assemblyName, string configuredName) =>
        configuredName.StartsWith("*", StringComparison.Ordinal)
            ? configuredName.Length > 1 && assemblyName.EndsWith(configuredName.Substring(1), StringComparison.OrdinalIgnoreCase)
            : assemblyName.Equals(configuredName, StringComparison.OrdinalIgnoreCase)
              || assemblyName.EndsWith("." + configuredName, StringComparison.OrdinalIgnoreCase);
}
