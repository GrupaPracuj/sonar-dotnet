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
public class ErrorResponsesShouldUseProblemDetailsTest
{
    [TestMethod]
    public void ErrorResponsesShouldUseProblemDetails() =>
        // The corpus declares ASP.NET Core stubs, which the concurrency wrapper would move to another namespace.
        new VerifierBuilder<CS.ErrorResponsesShouldUseProblemDetails>()
            .WithOptions(LanguageOptions.CSharpLatest)
            .WithConcurrentAnalysis(false)
            .WithBasePath("GP")
            .AddPaths("ErrorResponsesShouldUseProblemDetails.cs")
            .Verify();
}
