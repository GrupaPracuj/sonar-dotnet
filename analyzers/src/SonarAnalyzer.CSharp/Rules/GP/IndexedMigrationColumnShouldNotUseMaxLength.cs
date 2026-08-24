/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using System.Collections.Concurrent;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IndexedMigrationColumnShouldNotUseMaxLength : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0122";

    private const string MessageFormat = "Give indexed column '{0}' a bounded string length; SQL Server cannot index NVARCHAR(MAX).";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var candidates = new ConcurrentDictionary<string, MaxColumnCandidate>(StringComparer.Ordinal);
            var indexedColumns = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            start.RegisterNodeAction(c => Collect(c, candidates, indexedColumns), SyntaxKind.InvocationExpression);
            start.RegisterCompilationEndAction(c => Report(c, candidates.Values, indexedColumns));
        });

    private static void Collect(SonarSyntaxNodeReportingContext context,
                                ConcurrentDictionary<string, MaxColumnCandidate> candidates,
                                ConcurrentDictionary<string, byte> indexedColumns)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method || !IsFluentMigrator(method))
        {
            return;
        }

        if (method.Name == "AsString")
        {
            CollectMaxColumn(context, invocation, candidates);
        }
        else if (method.Name is "OnColumn" or "Column" or "Columns")
        {
            CollectIndexedColumns(context, invocation, indexedColumns);
        }
    }

    private static void CollectMaxColumn(SonarSyntaxNodeReportingContext context,
                                         InvocationExpressionSyntax invocation,
                                         ConcurrentDictionary<string, MaxColumnCandidate> candidates)
    {
        if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } length
            || context.Model.GetConstantValue(length) is not { HasValue: true, Value: int value }
            || value is not (-1 or int.MaxValue)
            || ChainInvocation(context.Model, invocation, "WithColumn") is not { } withColumn
            || ConstantString(context.Model, withColumn) is not { } column)
        {
            return;
        }

        var inlinePrimaryKey = invocation.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .TakeWhile(x => x.FirstAncestorOrSelf<StatementSyntax>() == invocation.FirstAncestorOrSelf<StatementSyntax>())
            .Any(x => context.Model.GetSymbolInfo(x).Symbol is IMethodSymbol { Name: "PrimaryKey" } primaryKey
                      && IsFluentMigrator(primaryKey));

        var table = ChainInvocation(context.Model, invocation, "Table") is { } tableInvocation
            ? ConstantString(context.Model, tableInvocation)
            : null;
        var schema = ChainInvocation(context.Model, invocation, "InSchema") is { } schemaInvocation
            ? ConstantString(context.Model, schemaInvocation)
            : string.Empty;
        var containingType = context.Model.GetEnclosingSymbol(invocation.SpanStart)?.ContainingType;
        var key = table is null || containingType is null ? null : ColumnKey(containingType, schema, table, column);
        var location = invocation.Expression is MemberAccessExpressionSyntax { Name: { } name }
            ? name.GetLocation()
            : invocation.GetLocation();
        var candidate = new MaxColumnCandidate(key, location, column, inlinePrimaryKey);
        candidates.TryAdd($"{location.SourceTree?.FilePath}|{location.SourceSpan.Start}", candidate);
    }

    private static void CollectIndexedColumns(SonarSyntaxNodeReportingContext context,
                                              InvocationExpressionSyntax invocation,
                                              ConcurrentDictionary<string, byte> indexedColumns)
    {
        if (!HasChainInvocation(context.Model, invocation, "Index")
            && !HasChainInvocation(context.Model, invocation, "PrimaryKey"))
        {
            return;
        }

        if (ChainInvocation(context.Model, invocation, "OnTable") is not { } onTable
            || ConstantString(context.Model, onTable) is not { } table)
        {
            return;
        }

        var schema = ChainInvocation(context.Model, invocation, "InSchema") is { } inSchema
            ? ConstantString(context.Model, inSchema)
            : string.Empty;
        if (context.Model.GetEnclosingSymbol(invocation.SpanStart)?.ContainingType is not { } containingType)
        {
            return;
        }

        foreach (var column in invocation.ArgumentList.Arguments
                     .Select(x => context.Model.GetConstantValue(x.Expression))
                     .Where(x => x is { HasValue: true, Value: string })
                     .Select(x => (string)x.Value))
        {
            indexedColumns.TryAdd(ColumnKey(containingType, schema, table, column), 0);
        }
    }

    private static void Report(SonarCompilationReportingContext context,
                               IEnumerable<MaxColumnCandidate> candidates,
                               ConcurrentDictionary<string, byte> indexedColumns)
    {
        foreach (var candidate in candidates
                     .Where(x => x.InlinePrimaryKey || x.Key is not null && indexedColumns.ContainsKey(x.Key))
                     .OrderBy(x => x.Location.SourceTree?.FilePath, StringComparer.Ordinal)
                     .ThenBy(x => x.Location.SourceSpan.Start))
        {
            context.ReportIssue(CSharpGeneratedCodeRecognizer.Instance, Rule, candidate.Location, messageArgs: new[] { candidate.Column });
        }
    }

    private static InvocationExpressionSyntax ChainInvocation(SemanticModel model,
                                                              InvocationExpressionSyntax invocation,
                                                              string methodName) =>
        ChainInvocations(invocation).FirstOrDefault(x =>
            model.GetSymbolInfo(x).Symbol is IMethodSymbol method
            && method.Name == methodName
            && IsFluentMigrator(method));

    private static bool HasChainInvocation(SemanticModel model, InvocationExpressionSyntax invocation, string methodName) =>
        ChainInvocation(model, invocation, methodName) is not null;

    private static IEnumerable<InvocationExpressionSyntax> ChainInvocations(InvocationExpressionSyntax invocation)
    {
        for (var current = invocation; current is not null;)
        {
            yield return current;
            current = current.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax receiver }
                ? receiver
                : null;
        }
    }

    private static string ConstantString(SemanticModel model, InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } expression
        && model.GetConstantValue(expression) is { HasValue: true, Value: string value }
            ? value
            : null;

    private static bool IsFluentMigrator(IMethodSymbol method) =>
        method.ContainingNamespace?.ToDisplayString() is { } containingNamespace
        && (containingNamespace == "FluentMigrator"
            || containingNamespace.StartsWith("FluentMigrator.", StringComparison.Ordinal));

    private static string ColumnKey(INamedTypeSymbol containingType, string schema, string table, string column) =>
        $"{containingType.ToDisplayString()}|{schema?.ToUpperInvariant()}|{table.ToUpperInvariant()}|{column.ToUpperInvariant()}";

    private readonly record struct MaxColumnCandidate(string Key, Location Location, string Column, bool InlinePrimaryKey);
}
