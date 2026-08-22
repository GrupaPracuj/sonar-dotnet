/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using System.Text.RegularExpressions;

namespace SonarAnalyzer.CSharp.Rules;

/// <summary>
/// Shallow reader for the hand-written T-SQL that GP services keep in string literals and hand to Dapper.
/// It is deliberately not a parser: it recognises the few shapes those literals actually take and gives up on
/// anything else, so a rule built on it under-reports rather than guesses. Every method returns empty or null
/// when the text does not match a shape it understands.
/// </summary>
internal static class GpSqlText
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    private static readonly Regex SqlStatement = new(@"\b(SELECT|INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE\s+INTO)\b", Options, Timeout);
    private static readonly Regex RowLimiter = new(@"\bTOP\s*[\(\s]\s*\d+|\bFETCH\s+NEXT\b|\bOFFSET\b", Options, Timeout);
    private static readonly Regex OrderByClause = new(@"\bORDER\s+BY\s+(?<list>.*?)(?=\bOFFSET\b|\bFETCH\b|\bFOR\s+XML\b|;|\z)", Options, Timeout);
    private static readonly Regex SelectList = new(@"\bSELECT\b(?<list>.*?)\bFROM\b", Options, Timeout);
    private static readonly Regex FromTable = new(@"\bFROM\s+(?<table>[A-Za-z0-9_\.\[\]]+)", Options, Timeout);
    private static readonly Regex WriteTarget = new(@"\b(?:INSERT\s+INTO|MERGE\s+INTO|UPDATE)\s+(?<table>[A-Za-z0-9_\.\[\]]+)", Options, Timeout);
    private static readonly Regex InsertColumns = new(@"\bINSERT\b(?:\s+INTO)?\s*(?:[A-Za-z0-9_\.\[\]]+)?\s*\(\s*(?<list>[^\)]*?)\s*\)\s*VALUES", Options, Timeout);
    private static readonly Regex UpdateAssignments = new(@"\bSET\s+(?<list>.*?)(?=\bWHERE\b|\bFROM\b|\bOUTPUT\b|;|\z)", Options, Timeout);
    private static readonly Regex JoinOrUnion = new(@"\bJOIN\b|\bUNION\b|\bAPPLY\b", Options, Timeout);
    private static readonly Regex LeadingListPrefix = new(@"^\s*(TOP\s*\(?\s*\d+\s*\)?|DISTINCT)\s+", Options, Timeout);
    private static readonly Regex TrailingDirection = new(@"\s+(ASC|DESC)\s*$", Options, Timeout);
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.ExplicitCapture, Timeout);

    private static readonly char[] NotInAColumnReference = ['(', ')', '*', '+', '-', '\'', ',', '='];

    internal static bool LooksLikeSql(string text) =>
        text is { Length: > 12 } && Match(SqlStatement, text).Success;

    internal static bool HasRowLimiter(string text) =>
        Match(RowLimiter, text).Success;

    internal static bool HasJoinOrUnion(string text) =>
        Match(JoinOrUnion, text).Success;

    /// <summary>
    /// Ordering terms of the first ORDER BY, each stripped to its bare column name. Empty when there is no
    /// ORDER BY, or when any term is not a plain column reference (a CASE, a function call, an ordinal, ...) -
    /// those are left alone rather than guessed at.
    /// </summary>
    internal static ImmutableArray<string> OrderByColumns(string text)
    {
        if (Match(OrderByClause, text) is not { Success: true } match)
        {
            return ImmutableArray<string>.Empty;
        }

        var columns = ImmutableArray.CreateBuilder<string>();
        foreach (var term in SplitTopLevel(match.Groups["list"].Value))
        {
            if (BareColumn(Replace(TrailingDirection, term)) is not { } column)
            {
                return ImmutableArray<string>.Empty;
            }

            columns.Add(column);
        }

        return columns.ToImmutable();
    }

    /// <summary>
    /// Table the statement reads from, normalised to the bare table name. Null when it reads from more than one
    /// thing, so a column is never attributed to the wrong table.
    /// </summary>
    internal static string ReadTable(string text) =>
        HasJoinOrUnion(text) || Match(FromTable, text) is not { Success: true } match
            ? null
            : NormalizeTable(match.Groups["table"].Value);

    internal static string WriteTable(string text) =>
        Match(WriteTarget, text) is { Success: true } match
            ? NormalizeTable(match.Groups["table"].Value)
            : null;

    /// <summary>Columns of the SELECT list. Empty unless the list is a plain enumeration of columns.</summary>
    internal static ImmutableArray<string> SelectedColumns(string text) =>
        Match(SelectList, text) is { Success: true } match ? PlainColumns(match.Groups["list"].Value) : ImmutableArray<string>.Empty;

    /// <summary>Columns an INSERT names, or the assigned columns of an UPDATE's SET list.</summary>
    internal static ImmutableArray<string> WrittenColumns(string text)
    {
        if (Match(InsertColumns, text) is { Success: true } insert)
        {
            return PlainColumns(insert.Groups["list"].Value);
        }

        if (Match(UpdateAssignments, text) is not { Success: true } update)
        {
            return ImmutableArray<string>.Empty;
        }

        var assigned = ImmutableArray.CreateBuilder<string>();
        foreach (var assignment in SplitTopLevel(update.Groups["list"].Value))
        {
            var separator = assignment.IndexOf('=');
            if (separator > 0 && BareColumn(assignment.Substring(0, separator)) is { } column)
            {
                assigned.Add(column);
            }
        }

        return assigned.ToImmutable();
    }

    private static ImmutableArray<string> PlainColumns(string list)
    {
        var columns = ImmutableArray.CreateBuilder<string>();
        foreach (var item in SplitTopLevel(list))
        {
            if (BareColumn(Replace(LeadingListPrefix, item)) is not { } column)
            {
                return ImmutableArray<string>.Empty;
            }

            columns.Add(column);
        }

        return columns.ToImmutable();
    }

    /// <summary>"c.[CreatedAtUtc]" -> "CreatedAtUtc"; null for anything that is not a single column reference.</summary>
    private static string BareColumn(string term)
    {
        var text = term.Trim();
        if (text.Length == 0 || text.IndexOfAny(NotInAColumnReference) >= 0)
        {
            return null;
        }

        var last = LastSegment(text).Trim().Trim('[', ']').Trim();
        return Identifier.IsMatch(last) && !IsKeyword(last) ? last : null;
    }

    private static string LastSegment(string text)
    {
        var segments = text.Split('.');
        return segments[segments.Length - 1];
    }

    private static bool IsKeyword(string word) =>
        word.Equals("NULL", StringComparison.OrdinalIgnoreCase)
        || word.Equals("AS", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTable(string table) =>
        LastSegment(table).Trim().Trim('[', ']').Trim().ToUpperInvariant();

    private static IEnumerable<string> SplitTopLevel(string list)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < list.Length; index++)
        {
            switch (list[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return list.Substring(start, index - start);
                    start = index + 1;
                    break;
                default:
                    break;
            }
        }

        if (start < list.Length && list.Substring(start).Trim().Length > 0)
        {
            yield return list.Substring(start);
        }
    }

    private static string Replace(Regex regex, string text)
    {
        try
        {
            return regex.Replace(text, string.Empty);
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
        }
    }

    private static Match Match(Regex regex, string text)
    {
        try
        {
            return regex.Match(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return System.Text.RegularExpressions.Match.Empty;
        }
    }
}
