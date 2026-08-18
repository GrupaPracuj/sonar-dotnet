/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ThreadStaticAttributeShouldBeOnStaticField : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0084";

    private const string MessageFormat = "'{0}' has 'System.ThreadStaticAttribute' but is not static - the attribute has no effect on instance fields.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.FieldDeclaration);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (FieldDeclarationSyntax)context.Node;
        if (declaration.Modifiers.Any(SyntaxKind.StaticKeyword)
            || declaration.AttributeLists.GetAttributes(KnownType.System_ThreadStaticAttribute, context.Model).FirstOrDefault() is null)
        {
            return;
        }

        foreach (var variable in declaration.Declaration.Variables)
        {
            context.ReportIssue(Rule, variable.Identifier, variable.Identifier.ValueText);
        }
    }
}
