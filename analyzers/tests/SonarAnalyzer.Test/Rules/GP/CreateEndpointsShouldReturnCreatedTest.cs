/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */
#if NET

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class CreateEndpointsShouldReturnCreatedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CreateEndpointsShouldReturnCreated>()
        .WithBasePath("GP")
        .WithOptions(LanguageOptions.CSharpLatest)
        .AddReferences([
            AspNetCoreMetadataReference.MicrosoftAspNetCoreHttpAbstractions,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreHttpResults,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcAbstractions,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcCore,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcViewFeatures,
        ]);

    [TestMethod]
    public void CreateEndpointsShouldReturnCreated_CS() =>
        builder.AddPaths("CreateEndpointsShouldReturnCreated.cs").Verify();
}

#endif
