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
public class ExplicitThrowShouldNotBeUsedInExceptionFilterTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ExplicitThrowShouldNotBeUsedInExceptionFilter>()
        .WithBasePath("GP")
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ExplicitThrowShouldNotBeUsedInExceptionFilter_CS() =>
        builder.AddPaths("ExplicitThrowShouldNotBeUsedInExceptionFilter.cs").Verify();
}
