/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationAuditColumnShouldUseDateTime2 : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0109";

    private const string MessageFormat = "Use AsDateTime2() for audit column '{0}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> AuditColumns = new(StringComparer.Ordinal)
    {
        "RowCreatedAtUtc",
        "RowUpdatedAtUtc",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "AsDateTime" } method
            || !IsFluentMigrator(method)
            || invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: InvocationExpressionSyntax withColumnInvocation,
                Name: { } methodName,
            }
            || context.Model.GetSymbolInfo(withColumnInvocation).Symbol is not IMethodSymbol { Name: "WithColumn" } withColumn
            || !IsFluentMigrator(withColumn)
            || withColumnInvocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } columnExpression
            || context.Model.GetConstantValue(columnExpression) is not { HasValue: true, Value: string columnName }
            || !AuditColumns.Contains(columnName))
        {
            return;
        }

        context.ReportIssue(Rule, methodName, columnName);
    }

    private static bool IsFluentMigrator(IMethodSymbol method) =>
        method.ContainingNamespace?.ToDisplayString() is { } containingNamespace
        && (containingNamespace == "FluentMigrator"
            || containingNamespace.StartsWith("FluentMigrator.", StringComparison.Ordinal));
}
