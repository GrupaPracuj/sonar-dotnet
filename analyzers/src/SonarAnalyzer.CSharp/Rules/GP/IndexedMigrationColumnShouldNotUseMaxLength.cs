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
            var boundedColumns = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            start.RegisterNodeAction(c => Collect(c, candidates, indexedColumns, boundedColumns), SyntaxKind.InvocationExpression);
            start.RegisterCompilationEndAction(c => Report(c, candidates.Values, indexedColumns, boundedColumns));
        });

    private static void Collect(SonarSyntaxNodeReportingContext context,
                                ConcurrentDictionary<string, MaxColumnCandidate> candidates,
                                ConcurrentDictionary<string, byte> indexedColumns,
                                ConcurrentDictionary<string, byte> boundedColumns)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method || !IsFluentMigrator(method))
        {
            return;
        }

        if (method.Name == "AsString")
        {
            CollectStringColumn(context, invocation, candidates, boundedColumns);
        }
        else if (method.Name is "OnColumn" or "Column" or "Columns")
        {
            CollectIndexedColumns(context, invocation, indexedColumns);
        }
        else if (method.Name == "Indexed")
        {
            CollectFluentIndexedColumn(context, invocation, indexedColumns);
        }
    }

    private static void CollectStringColumn(SonarSyntaxNodeReportingContext context,
                                            InvocationExpressionSyntax invocation,
                                            ConcurrentDictionary<string, MaxColumnCandidate> candidates,
                                            ConcurrentDictionary<string, byte> boundedColumns)
    {
        if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } length
            || context.Model.GetConstantValue(length) is not { HasValue: true, Value: int value }
            || ColumnName(context.Model, invocation) is not { } column)
        {
            return;
        }

        var key = ColumnKey(context.Model, invocation, column);
        if (value is not (-1 or int.MaxValue))
        {
            // Bounding the same column in a later migration is exactly how this defect gets fixed, so a bounded
            // declaration anywhere cancels the candidate. Migration order is not recoverable from the source, and a
            // column declared both ways across the history is one somebody has already dealt with.
            if (key is not null)
            {
                boundedColumns.TryAdd(key, 0);
            }

            return;
        }

        var inlineIndex = invocation.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .TakeWhile(x => x.FirstAncestorOrSelf<StatementSyntax>() == invocation.FirstAncestorOrSelf<StatementSyntax>())
            .Any(x => context.Model.GetSymbolInfo(x).Symbol is IMethodSymbol { Name: "PrimaryKey" or "Indexed" } indexing
                      && IsFluentMigrator(indexing));

        var location = invocation.Expression is MemberAccessExpressionSyntax { Name: { } name }
            ? name.GetLocation()
            : invocation.GetLocation();
        var candidate = new MaxColumnCandidate(key, location, column, inlineIndex);
        candidates.TryAdd($"{location.SourceTree?.FilePath}|{location.SourceSpan.Start}", candidate);
    }

    // Indexed() indexes the column being defined, so both the column and the table come from its own chain.
    private static void CollectFluentIndexedColumn(SonarSyntaxNodeReportingContext context,
                                                   InvocationExpressionSyntax invocation,
                                                   ConcurrentDictionary<string, byte> indexedColumns)
    {
        if (ColumnName(context.Model, invocation) is { } column
            && ColumnKey(context.Model, invocation, column) is { } key)
        {
            indexedColumns.TryAdd(key, 0);
        }
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

        foreach (var column in invocation.ArgumentList.Arguments
                     .Select(x => context.Model.GetConstantValue(x.Expression))
                     .Where(x => x is { HasValue: true, Value: string })
                     .Select(x => (string)x.Value))
        {
            indexedColumns.TryAdd(ColumnKey(schema, table, column), 0);
        }
    }

    private static void Report(SonarCompilationReportingContext context,
                               IEnumerable<MaxColumnCandidate> candidates,
                               ConcurrentDictionary<string, byte> indexedColumns,
                               ConcurrentDictionary<string, byte> boundedColumns)
    {
        foreach (var candidate in candidates
                     .Where(x => x.Key is null
                                     ? x.InlineIndex
                                     : !boundedColumns.ContainsKey(x.Key) && (x.InlineIndex || indexedColumns.ContainsKey(x.Key)))
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

    // The key deliberately leaves out the migration class. A column is declared in one migration and indexed in a
    // later one - the normal shape of a migration history, and the shape that used to hide this defect from the rule.
    private static string ColumnKey(string schema, string table, string column) =>
        $"{schema?.ToUpperInvariant()}|{table.ToUpperInvariant()}|{column.ToUpperInvariant()}";

    private static string ColumnKey(SemanticModel model, InvocationExpressionSyntax invocation, string column)
    {
        var tableInvocation = ChainInvocation(model, invocation, "Table") ?? ChainInvocation(model, invocation, "OnTable");
        if (tableInvocation is null || ConstantString(model, tableInvocation) is not { } table)
        {
            return null;
        }

        var schema = ChainInvocation(model, invocation, "InSchema") is { } schemaInvocation
            ? ConstantString(model, schemaInvocation)
            : string.Empty;
        return ColumnKey(schema, table, column);
    }

    private static string ColumnName(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        foreach (var name in new[] { "WithColumn", "AddColumn", "AlterColumn" })
        {
            if (ChainInvocation(model, invocation, name) is { } column && ConstantString(model, column) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private readonly record struct MaxColumnCandidate(string Key, Location Location, string Column, bool InlineIndex);
}
