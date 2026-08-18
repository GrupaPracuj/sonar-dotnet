/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UsingShouldNotUseThrowingObjectInitializer : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0089";

    private const string MessageFormat = "This 'using' constructs '{0}' via an object initializer - if a member assignment throws, the instance is "
                                          + "never bound and 'Dispose' is never called. Assign the risky members in separate statements after construction.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeUsingDeclaration, SyntaxKind.LocalDeclarationStatement);
        context.RegisterNodeAction(AnalyzeUsingStatement, SyntaxKind.UsingStatement);
    }

    // using var x = new Foo { ... };
    private static void AnalyzeUsingDeclaration(SonarSyntaxNodeReportingContext context)
    {
        var localDeclaration = (LocalDeclarationStatementSyntax)context.Node;
        if (localDeclaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
        {
            AnalyzeVariables(context, localDeclaration.Declaration);
        }
    }

    // using (var x = new Foo { ... }) { ... }
    private static void AnalyzeUsingStatement(SonarSyntaxNodeReportingContext context)
    {
        var usingStatement = (UsingStatementSyntax)context.Node;
        if (usingStatement.Declaration is { } declaration)
        {
            AnalyzeVariables(context, declaration);
        }
    }

    private static void AnalyzeVariables(SonarSyntaxNodeReportingContext context, VariableDeclarationSyntax declaration)
    {
        foreach (var variable in declaration.Variables)
        {
            if (variable.Initializer?.Value is ObjectCreationExpressionSyntax
                {
                    Initializer: { RawKind: (int)SyntaxKind.ObjectInitializerExpression, Expressions.Count: > 0 } initializer
                } objectCreation
                && initializer.Expressions.Any(x => HasRiskyAssignment(x, context.Model)))
            {
                context.ReportIssue(Rule, objectCreation, variable.Identifier.ValueText);
            }
        }
    }

    private static bool HasRiskyAssignment(ExpressionSyntax memberInitializer, SemanticModel model) =>
        memberInitializer is AssignmentExpressionSyntax { Left: { } target, Right: { } value }
        && (SetterMayThrow(target, model) || IsRisky(value));

    private static bool SetterMayThrow(ExpressionSyntax target, SemanticModel model) =>
        model.GetSymbolInfo(target).Symbol is IPropertySymbol property && !IsSourceAutoProperty(property);

    private static bool IsSourceAutoProperty(IPropertySymbol property) =>
        property.SetMethod?.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<AccessorDeclarationSyntax>()
            .Any(x => x.Body is null
                      && x.ExpressionBody is null
                      && x.Parent?.Parent is PropertyDeclarationSyntax declaration
                      && !declaration.Modifiers.Any(SyntaxKind.AbstractKeyword)) == true;

    // The "safe, never flag" list is deliberately narrow: a literal, or a bare identifier/parameter read, cannot realistically
    // throw. Anything else - a call, a member access chain, a cast, a binary expression, ... - can, so it is treated as risky.
    private static bool IsRisky(ExpressionSyntax value) =>
        value switch
        {
            LiteralExpressionSyntax => false,
            IdentifierNameSyntax => false,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax } => false,
            _ => true,
        };
}
