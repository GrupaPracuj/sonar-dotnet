/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RowLimitedQueryShouldOrderDeterministically : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0118";

    private const string MessageFormat =
        "This query takes a subset of rows ordered only by {0}, so ties are broken arbitrarily. Add a unique column to the ORDER BY.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // A timestamp is the classic near-unique-but-not-unique sort key: unique enough that ties never show up while
    // testing, common enough that they show up in production. Ordering only by such columns and then cutting the
    // result set is what makes the outcome depend on the query plan.
    private static readonly string[] TemporalMarkers = ["DATE", "TIME", "UTC", "STAMP", "CREATED", "MODIFIED", "UPDATED"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    // Raw string literals ("""...""") are LiteralExpressionSyntax of this same kind, so one registration covers
    // the quoted, verbatim and raw forms the SQL literals are written in.
    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeStringLiteral, SyntaxKind.StringLiteralExpression);

    private static void AnalyzeStringLiteral(SonarSyntaxNodeReportingContext context)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        if (context.Model.GetConstantValue(literal) is not { HasValue: true, Value: string sql }
            || !GpSqlText.LooksLikeSql(sql)
            || !GpSqlText.HasRowLimiter(sql))
        {
            return;
        }

        var columns = GpSqlText.OrderByColumns(sql);
        if (!columns.IsDefaultOrEmpty && columns.All(IsTemporal))
        {
            context.ReportIssue(Rule, literal, Describe(columns));
        }
    }

    private static bool IsTemporal(string column)
    {
        var upper = column.ToUpperInvariant();
        return Array.Exists(TemporalMarkers, upper.Contains);
    }

    private static string Describe(ImmutableArray<string> columns) =>
        string.Join(" and ", columns.Select(x => $"'{x}'"));
}
