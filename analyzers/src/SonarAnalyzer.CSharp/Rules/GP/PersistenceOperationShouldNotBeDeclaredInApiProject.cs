/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PersistenceOperationShouldNotBeDeclaredInApiProject : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0133";

    private const string MessageFormat = "Declare '{0}' in the data access project - an API assembly is not the place for a persistence operation.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.ClassDeclaration);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        if (ControllersShouldNotUseInfrastructureDirectly.IsApiProject(context.Compilation)
            && context.Model.GetDeclaredSymbol(declaration) is { IsAbstract: false } type
            && ControllersShouldNotUseInfrastructureDirectly.IsPersistenceOperation(type))
        {
            context.ReportIssue(Rule, declaration.Identifier, type.Name);
        }
    }
}
