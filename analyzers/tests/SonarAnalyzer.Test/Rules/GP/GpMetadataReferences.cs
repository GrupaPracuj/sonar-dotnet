/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */
#if NET

using System.IO;

namespace SonarAnalyzer.Test.Rules.GP;

/// <summary>
/// Metadata references the GP rule tests need and <see cref="AspNetCoreMetadataReference"/> does not expose.
/// The shared-framework folder is derived from a reference that class already resolves, so this stays on public
/// API and does not have to duplicate the SDK-discovery logic (which is internal to the test framework).
/// </summary>
internal static class GpMetadataReferences
{
    public static MetadataReference MicrosoftAspNetCoreAuthorization { get; } =
        AspNetCoreSibling("Microsoft.AspNetCore.Authorization.dll");

    public static MetadataReference MicrosoftAspNetCoreMetadata { get; } =
        AspNetCoreSibling("Microsoft.AspNetCore.Metadata.dll");

    private static MetadataReference AspNetCoreSibling(string assemblyName)
    {
        var known = (PortableExecutableReference)AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcCore;
        var folder = Path.GetDirectoryName(known.FilePath);
        return MetadataReference.CreateFromFile(Path.Combine(folder, assemblyName));
    }
}

#endif
