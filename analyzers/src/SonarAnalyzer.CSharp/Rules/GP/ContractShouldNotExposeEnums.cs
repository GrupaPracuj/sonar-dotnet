/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotExposeEnums : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0068";

    private const string MessageFormat =
        "'{0}' is exposed by a contract. Do not use enums in contracts because producers and consumers evolve independently.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var contractEnums = GpContractEnums.Create(GpSemanticContractDetector.GetOrCreate(start.Compilation));
            if (!contractEnums.IsEmpty)
            {
                start.RegisterNodeAction(c => AnalyzeEnum(c, contractEnums), SyntaxKind.EnumDeclaration);
            }
        });

    private static void AnalyzeEnum(SonarSyntaxNodeReportingContext context, GpContractEnums contractEnums)
    {
        if (context.Node is EnumDeclarationSyntax declaration
            && context.Model.GetDeclaredSymbol(declaration) is { } enumType
            && contractEnums.IsUsedByAContract(enumType))
        {
            context.ReportIssue(Rule, declaration.Identifier, enumType.Name);
        }
    }
}
