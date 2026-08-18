/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotInheritDomainType : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0057";

    private const string MessageFormat = "'{0}' is a domain type - a contract that inherits it publishes the whole entity.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("entityBaseTypes", PropertyType.String, "Comma-separated base types whose descendants are entities, e.g. Entity,AggregateRoot", "")]
    public string EntityBaseTypes { get; set; } = string.Empty;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var entities = GpEntityTypes.Create(start.Compilation, EntityBaseTypes);
            var contracts = GpSemanticContractDetector.GetOrCreate(start.Compilation);
            start.RegisterNodeAction(c => AnalyzeTypeDeclaration(c, entities, contracts), SyntaxKind.ClassDeclaration, SyntaxKindEx.RecordDeclaration);
        });

    private static void AnalyzeTypeDeclaration(SonarSyntaxNodeReportingContext context, GpEntityTypes entities, GpSemanticContractDetector contracts)
    {
        if (context.Node is not TypeDeclarationSyntax { BaseList: not null } declaration
            || context.Model.GetDeclaredSymbol(declaration) is not { BaseType: { } baseType } type
            || !contracts.IsContract(type)
            // Only a base class counts. Inheriting another contract or implementing a marker interface is fine.
            || baseType.SpecialType == SpecialType.System_Object
            || !entities.IsEntity(baseType))
        {
            return;
        }

        context.ReportIssue(Rule, declaration.Identifier, baseType.Name);
    }
}
