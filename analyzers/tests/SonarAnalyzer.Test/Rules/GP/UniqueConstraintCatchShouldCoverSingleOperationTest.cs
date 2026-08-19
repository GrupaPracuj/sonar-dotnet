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
public class UniqueConstraintCatchShouldCoverSingleOperationTest
{
    [TestMethod]
    public void UniqueConstraintCatchShouldCoverSingleOperation() =>
        // The corpus declares framework and Juno stubs, which the concurrency wrapper would relocate.
        new VerifierBuilder<CS.UniqueConstraintCatchShouldCoverSingleOperation>()
            .WithOptions(LanguageOptions.CSharpLatest)
            .AddReferences(MetadataReferenceFacade.SystemData)
            .WithConcurrentAnalysis(false)
            .WithBasePath("GP")
            .AddPaths("UniqueConstraintCatchShouldCoverSingleOperation.cs")
            .Verify();
}
