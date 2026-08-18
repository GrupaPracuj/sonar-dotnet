/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DatabaseFunctionShouldOnlyBeCalledInQuery : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0086";

    private const string MessageFormat = "'{0}' is only meaningful inside a query translated by Entity Framework - call it directly inside an inline Queryable lambda.";

    // The type behind EF.Functions; no KnownType constant exists for this EF-specific type in this codebase, so it
    // is compared by display string directly, the same way GP0017 compares against GP.Juno.Dates.LocalDate.
    private const string DbFunctionsType = "Microsoft.EntityFrameworkCore.DbFunctions";
    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.InvocationExpression);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !IsDbFunctionsMember(method)
            || IsInsideInlineQueryableLambda(invocation, context.Model))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.Name);
    }

    // Like/DateDiffDay/... are extension methods on DbFunctions (the type behind EF.Functions). GetSymbolInfo
    // resolves an extension method called via instance syntax in its reduced form, so the receiver has to be read
    // from ReceiverType rather than ContainingType (which points at the static class declaring the extension, e.g.
    // DbFunctionsExtensions) - the same technique GpHttpCallHelper already uses for this exact reason.
    private static bool IsDbFunctionsMember(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString() == DbFunctionsType
        || (method.IsExtensionMethod && method.ReceiverType?.ToDisplayString() == DbFunctionsType);

    private static bool IsInsideInlineQueryableLambda(SyntaxNode node, SemanticModel model) =>
        node.Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Any(lambda =>
                lambda.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax queryInvocation }
                && model.GetSymbolInfo(queryInvocation).Symbol is IMethodSymbol queryMethod
                && queryMethod.ContainingType.ToDisplayString() == "System.Linq.Queryable");
}
