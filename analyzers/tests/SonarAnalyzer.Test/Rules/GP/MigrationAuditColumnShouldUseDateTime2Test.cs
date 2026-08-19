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
public class MigrationAuditColumnShouldUseDateTime2Test
{
    [TestMethod]
    public void MigrationAuditColumnShouldUseDateTime2_CodeFix() =>
        // The corpus declares FluentMigrator stubs, which the concurrency wrapper would move to another namespace.
        new VerifierBuilder<CS.MigrationAuditColumnShouldUseDateTime2>()
            .WithOptions(LanguageOptions.CSharpLatest)
            .WithConcurrentAnalysis(false)
            .WithBasePath("GP")
            .AddPaths("MigrationAuditColumnShouldUseDateTime2.cs")
            .WithCodeFix<CS.MigrationAuditColumnShouldUseDateTime2CodeFix>()
            .WithCodeFixedPaths("MigrationAuditColumnShouldUseDateTime2.Fixed.cs")
            .VerifyCodeFix();
}
