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
public sealed class SqlColumnNamesShouldBeConsistent : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0117";

    private const string MessageFormat =
        "Column '{0}' is read from '{1}' but never written to it anywhere in this project. Check it against the table's schema.";

    /// <summary>
    /// Enough written columns that the project's INSERT can be taken as a picture of the table rather than a
    /// fragment of it. Below this the read side is not judged at all.
    /// </summary>
    private const int MinimumWrittenColumns = 3;

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    /// <summary>
    /// Columns a project legitimately reads without ever writing: surrogate keys, concurrency tokens and audit
    /// stamps are produced by the database or by a migration, so their absence from an INSERT means nothing.
    /// </summary>
    private static readonly HashSet<string> DatabaseGenerated = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id",
        "RowId",
        "Version",
        "RowVersion",
        "Timestamp",
        "CreatedAt",
        "CreatedAtUtc",
        "CreatedDate",
        "CreatedDateUtc",
        "InsertedAt",
        "InsertedAtUtc",
        "ModifiedAt",
        "ModifiedAtUtc",
        "UpdatedAt",
        "UpdatedAtUtc",
        "LastModified",
        "LastModifiedUtc",
        "RowCreatedBy",
        "RowCreatedAtUtc",
        "RowUpdatedBy",
        "RowUpdatedAtUtc",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var tables = new ConcurrentDictionary<string, TableUsage>(StringComparer.OrdinalIgnoreCase);
            start.RegisterNodeAction(x => Collect(x, tables), SyntaxKind.StringLiteralExpression);
            start.RegisterCompilationEndAction(x => Report(x, tables));
        });

    private static void Collect(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, TableUsage> tables)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        if (context.Model.GetConstantValue(literal) is not { HasValue: true, Value: string sql }
            || !GpSqlText.LooksLikeSql(sql))
        {
            return;
        }

        if (GpSqlText.WriteTable(sql) is { } writeTable && GpSqlText.WrittenColumns(sql) is { IsDefaultOrEmpty: false } written)
        {
            var usage = tables.GetOrAdd(writeTable, _ => new TableUsage());
            lock (usage.Gate)
            {
                foreach (var column in written)
                {
                    usage.Written.Add(column);
                }
            }
        }

        if (GpSqlText.ReadTable(sql) is { } readTable && GpSqlText.SelectedColumns(sql) is { IsDefaultOrEmpty: false } selected)
        {
            var usage = tables.GetOrAdd(readTable, _ => new TableUsage());
            lock (usage.Gate)
            {
                usage.Reads.Add(new SelectStatement(selected, literal.GetLocation()));
            }
        }
    }

    private static void Report(SonarCompilationReportingContext context, ConcurrentDictionary<string, TableUsage> tables)
    {
        foreach (var table in tables)
        {
            var usage = table.Value;
            if (usage.Written.Count < MinimumWrittenColumns)
            {
                continue;
            }

            foreach (var read in usage.Reads)
            {
                foreach (var column in Unmatched(read, usage.Written))
                {
                    context.ReportIssue(Rule, read.Location, column, table.Key);
                }
            }
        }
    }

    /// <summary>
    /// A statement where most of the SELECT list is unknown is not describing typos - it is reading a table this
    /// project only ever writes a slice of. Only a minority of unknown columns is treated as a finding.
    /// </summary>
    private static ImmutableArray<string> Unmatched(SelectStatement read, HashSet<string> written)
    {
        var unmatched = read.Columns
            .Where(x => !written.Contains(x) && !DatabaseGenerated.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        return unmatched.Length * 2 < read.Columns.Length ? unmatched : ImmutableArray<string>.Empty;
    }

    private sealed class TableUsage
    {
        public object Gate { get; } = new object();

        public HashSet<string> Written { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public List<SelectStatement> Reads { get; } = new List<SelectStatement>();
    }

    private sealed class SelectStatement
    {
        public SelectStatement(ImmutableArray<string> columns, Location location)
        {
            Columns = columns;
            Location = location;
        }

        public ImmutableArray<string> Columns { get; }

        public Location Location { get; }
    }
}
