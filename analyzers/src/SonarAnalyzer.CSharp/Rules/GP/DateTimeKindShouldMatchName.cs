/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeKindShouldMatchName : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0018";

    private const string MessageFormat = "'{0}' is named as {1} time, but is constructed with DateTimeKind.{2}.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (!ObjectCreationFactory.TryCreate(context.Node, out var creation)
            || creation.MethodSymbol(context.Model) is not { } ctor
            || !ctor.IsInType(KnownType.System_DateTime)
            || creation.ArgumentList is not { } argumentList)
        {
            return;
        }

        var kindArgument = argumentList.Arguments.FirstOrDefault(x => context.Model.GetTypeInfo(x.Expression).Type.Is(KnownType.System_DateTimeKind));
        if (kindArgument is not null && ExtractKind(kindArgument.Expression) is { } kind)
        {
            Check(context, creation.Expression, kind);
        }
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "SpecifyKind" } method
            && method.IsInType(KnownType.System_DateTime)
            && invocation.ArgumentList.Arguments.Count == 2
            && ExtractKind(invocation.ArgumentList.Arguments[1].Expression) is { } kind)
        {
            Check(context, invocation, kind);
        }
    }

    private static void Check(SonarSyntaxNodeReportingContext context, ExpressionSyntax expression, string kind)
    {
        if (TargetName(expression) is not { Length: > 0 } name)
        {
            return;
        }

        // Unspecified contradicts either name: it says the value carries no timezone information at all, while both
        // "Utc" and "Local" in a name promise that it does.
        if (GpIdentifierWords.ContainsWord(name, "Utc") && kind is "Local" or "Unspecified")
        {
            context.ReportIssue(Rule, expression, name, "UTC", kind);
        }
        else if (GpIdentifierWords.ContainsWord(name, "Local") && kind is "Utc" or "Unspecified")
        {
            context.ReportIssue(Rule, expression, name, "local", kind);
        }
    }

    private static string ExtractKind(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Utc" or "Local" or "Unspecified" } memberAccess
            ? memberAccess.Name.Identifier.ValueText
            : null;

    // Walks up from a DateTime-producing expression to find the name of the property/field/variable/method it
    // feeds into, so the constructed Kind can be compared against what that name implies (e.g. "ExpirationDateUtc").
    private static string TargetName(ExpressionSyntax expression) =>
        expression.Parent switch
        {
            AssignmentExpressionSyntax { Right: var right } assignment when right == expression => NameOf(assignment.Left),
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } => declarator.Identifier.ValueText,
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => property.Identifier.ValueText,
            ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax property } => property.Identifier.ValueText,
            ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax method } => method.Identifier.ValueText,
            ReturnStatementSyntax returnStatement => EnclosingMemberName(returnStatement),
            _ => null
        };

    private static string NameOf(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null
        };

    private static string EnclosingMemberName(SyntaxNode node) =>
        node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault() switch
        {
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            _ => null
        };
}
