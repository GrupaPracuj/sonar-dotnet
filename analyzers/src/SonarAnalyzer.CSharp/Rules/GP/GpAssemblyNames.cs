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
    internal static bool Matches(string assemblyName, string configuredName) =>
        assemblyName.Equals(configuredName, StringComparison.OrdinalIgnoreCase)
        || assemblyName.EndsWith("." + configuredName, StringComparison.OrdinalIgnoreCase);
}
