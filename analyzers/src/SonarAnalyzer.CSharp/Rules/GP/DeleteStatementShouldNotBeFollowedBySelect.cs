/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeleteStatementShouldNotBeFollowedBySelect : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0121";

    private const string MessageFormat = "Replace SELECT with FROM so the DELETE predicate belongs to the DELETE statement.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeLiteral, SyntaxKind.StringLiteralExpression);

    private static void AnalyzeLiteral(SonarSyntaxNodeReportingContext context)
    {
        if (context.Model.GetConstantValue(context.Node) is { HasValue: true, Value: string sql }
            && HasDetachedSelectAfterDelete(sql))
        {
            context.ReportIssue(Rule, context.Node);
        }
    }

    private static bool HasDetachedSelectAfterDelete(string sql)
    {
        var firstWordSeen = false;
        foreach (var token in TopLevelTokens(sql))
        {
            if (!firstWordSeen)
            {
                if (token != "DELETE")
                {
                    return false;
                }

                firstWordSeen = true;
            }
            else if (token == "SELECT")
            {
                return true;
            }
            else if (token is "FROM" or "WHERE" or "OUTPUT" or ";")
            {
                return false;
            }
        }

        return false;
    }

    private static IEnumerable<string> TopLevelTokens(string sql)
    {
        var depth = 0;
        for (var index = 0; index < sql.Length;)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
            }
            else if (StartsWith(sql, index, "--"))
            {
                index = SkipLineComment(sql, index + 2);
            }
            else if (StartsWith(sql, index, "/*"))
            {
                index = SkipBlockComment(sql, index + 2);
            }
            else if (sql[index] == '\'')
            {
                index = SkipQuoted(sql, index + 1, '\'', "''");
            }
            else if (sql[index] == '"')
            {
                index = SkipQuoted(sql, index + 1, '"', "\"\"");
            }
            else if (sql[index] == '[')
            {
                index = SkipQuoted(sql, index + 1, ']', "]]");
            }
            else if (sql[index] == '(')
            {
                depth++;
                index++;
            }
            else if (sql[index] == ')')
            {
                depth = Math.Max(0, depth - 1);
                index++;
            }
            else if (sql[index] == ';')
            {
                if (depth == 0)
                {
                    yield return ";";
                }

                index++;
            }
            else if (char.IsLetter(sql[index]) || sql[index] == '_')
            {
                var start = index++;
                while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] is '_' or '$' or '#'))
                {
                    index++;
                }

                if (depth == 0)
                {
                    yield return sql.Substring(start, index - start).ToUpperInvariant();
                }
            }
            else
            {
                index++;
            }
        }
    }

    private static int SkipLineComment(string text, int index)
    {
        while (index < text.Length && text[index] is not ('\r' or '\n'))
        {
            index++;
        }

        return index;
    }

    private static int SkipBlockComment(string text, int index)
    {
        var depth = 1;
        while (index < text.Length && depth > 0)
        {
            if (StartsWith(text, index, "/*"))
            {
                depth++;
                index += 2;
            }
            else if (StartsWith(text, index, "*/"))
            {
                depth--;
                index += 2;
            }
            else
            {
                index++;
            }
        }

        return index;
    }

    private static int SkipQuoted(string text, int index, char terminator, string escapedTerminator)
    {
        while (index < text.Length)
        {
            if (StartsWith(text, index, escapedTerminator))
            {
                index += escapedTerminator.Length;
            }
            else if (text[index] == terminator)
            {
                return index + 1;
            }
            else
            {
                index++;
            }
        }

        return index;
    }

    private static bool StartsWith(string text, int index, string value) =>
        index + value.Length <= text.Length
        && string.CompareOrdinal(text, index, value, 0, value.Length) == 0;
}
