/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SynchronousApiDatabaseQueryShouldBeBounded : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0127";

    private const string MessageFormat = "Bound the database result set used by this synchronous API path.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.InvocationExpression);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!GpSynchronousApiReachability.IsReachable(context.Model, invocation)
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (GpDatabaseCallHelper.IsDapperCollectionQuery(method)
            && GpDatabaseCallHelper.TryGetDapperSql(context.Model, invocation, method, out var sql)
            && !GpDatabaseCallHelper.IsResultSetBounded(sql))
        {
            context.ReportIssue(Rule, invocation);
        }
        else if (GpDatabaseCallHelper.IsEfCollectionMaterializer(method)
                 && GpDatabaseCallHelper.EfQueryBound(context.Model, invocation, method) == GpQueryBound.Unbounded)
        {
            context.ReportIssue(Rule, invocation);
        }
    }
}
