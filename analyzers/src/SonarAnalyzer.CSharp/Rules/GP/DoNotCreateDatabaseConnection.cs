/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotCreateDatabaseConnection : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0035";

    private const string MessageFormat = "Obtain the connection from Juno (IAdoConnectionFactory / IDbExecute) instead of creating it directly with '{0}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && GpJunoTypes.DerivesFrom(type, "System.Data.Common.DbConnection"))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (IsInsideJuno(context)
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "CreateConnection" } method
            || !GpJunoTypes.DerivesFrom(method.ContainingType, "System.Data.Common.DbProviderFactory"))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, $"{method.ContainingType.Name}.{method.Name}");
    }

    private static bool IsInsideJuno(SonarSyntaxNodeReportingContext context)
    {
        var containingNamespace = context.Model.GetEnclosingSymbol(context.Node.SpanStart)?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return containingNamespace == "GP.Juno" || containingNamespace.StartsWith("GP.Juno.", StringComparison.Ordinal);
    }
}
