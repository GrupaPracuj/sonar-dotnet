/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationDdlShouldUseFluentMigratorExpressions : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0119";

    private const string MessageFormat = "Replace raw CREATE TABLE or CREATE INDEX DDL with FluentMigrator expressions.";
    private const string MigrationType = "FluentMigrator.Migration";
    private const string ExecuteNamespace = "FluentMigrator.Builders.Execute";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "Sql" } method
            || !IsFluentMigratorExecute(method)
            || context.Model.GetEnclosingSymbol(invocation.SpanStart)?.ContainingType is not { } containingType
            || !GpJunoTypes.DerivesFrom(containingType, MigrationType)
            || SqlArgument(invocation, method) is not { } expression
            || SqlText(context.Model, expression) is not { } sql
            || !ContainsUnsupportedDdl(sql))
        {
            return;
        }

        context.ReportIssue(Rule, expression);
    }

    private static bool IsFluentMigratorExecute(IMethodSymbol method) =>
        method.ContainingNamespace?.ToDisplayString() is { } containingNamespace
        && (containingNamespace == ExecuteNamespace
            || containingNamespace.StartsWith($"{ExecuteNamespace}.", StringComparison.Ordinal));

    private static ExpressionSyntax SqlArgument(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        for (var index = 0; index < invocation.ArgumentList.Arguments.Count; index++)
        {
            var argument = invocation.ArgumentList.Arguments[index];
            var parameter = argument.NameColon is { Name.Identifier.ValueText: var name }
                ? method.Parameters.FirstOrDefault(x => x.Name == name)
                : index < method.Parameters.Length ? method.Parameters[index] : null;
            if (parameter?.Type.SpecialType == SpecialType.System_String)
            {
                return argument.Expression;
            }
        }

        return null;
    }

    private static string SqlText(SemanticModel model, ExpressionSyntax expression)
    {
        if (model.GetConstantValue(expression) is { HasValue: true, Value: string constant })
        {
            return constant;
        }

        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            return string.Join(" ", interpolated.Contents
                .OfType<InterpolatedStringTextSyntax>()
                .Select(x => x.TextToken.ValueText));
        }

        // FN: dynamically assembled SQL is intentionally ignored because its statement kind cannot be established.
        return null;
    }

    private static bool ContainsUnsupportedDdl(string sql)
    {
        var words = SqlWords(sql).ToArray();
        if (words.Length < 2 || words.Any(x => x.StartsWith("#", StringComparison.Ordinal)) || words[0] != "CREATE")
        {
            return false;
        }

        var index = 1;
        while (index < words.Length && words[index] is "UNIQUE" or "CLUSTERED" or "NONCLUSTERED")
        {
            index++;
        }

        // FN: later statements are ignored because CREATE TABLE inside a procedure or data-migration script cannot
        // be replaced with a FluentMigrator schema expression without changing the operation.
        return index < words.Length && words[index] is "TABLE" or "INDEX";
    }

    private static IEnumerable<string> SqlWords(string sql)
    {
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
            else if (char.IsLetter(sql[index]) || sql[index] == '_')
            {
                var start = index++;
                while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] == '_'))
                {
                    index++;
                }

                yield return sql.Substring(start, index - start).ToUpperInvariant();
            }
            else if (sql[index] == '#' && index + 1 < sql.Length && (char.IsLetter(sql[index + 1]) || sql[index + 1] == '_'))
            {
                var start = index++;
                while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] is '_' or '#'))
                {
                    index++;
                }

                yield return sql.Substring(start, index - start).ToUpperInvariant();
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
