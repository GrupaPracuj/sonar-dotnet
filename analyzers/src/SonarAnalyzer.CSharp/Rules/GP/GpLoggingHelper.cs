/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

internal static class GpLoggingHelper
{
    private const string MicrosoftLogger = "Microsoft.Extensions.Logging.ILogger";

    private static readonly HashSet<string> LoggingContainingTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.Logging.LoggerExtensions", // ILogger.LogInformation/LogWarning/... are extension methods declared here.
        "Serilog.Log",
        "Serilog.ILogger",
    };

    // A list of type names only ever covers the loggers we thought of. Anything declared on ILogger, or taking one as
    // the receiver of an extension method, is a logging call by construction - which is how LoggerExtensions itself is
    // shaped, and how a project that adds its own Log(...) overload shapes it too. Without this, a service logging
    // through its own extension is invisible to every rule here.
    internal static bool IsLoggingCall(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
        && (LoggingContainingTypes.Contains(method.ContainingType?.ToDisplayString() ?? string.Empty)
            || IsMicrosoftLogger(method.ContainingType)
            || IsLoggerExtension(method));

    private static bool IsLoggerExtension(IMethodSymbol method) =>
        (method.ReducedFrom ?? method) is { IsExtensionMethod: true, Parameters: { Length: > 0 } parameters }
        && IsMicrosoftLogger(parameters[0].Type);

    private static bool IsMicrosoftLogger(ITypeSymbol type) =>
        type is not null
        && (type.OriginalDefinition.ToDisplayString() is MicrosoftLogger or MicrosoftLogger + "<TCategoryName>"
            || type.AllInterfaces.Any(x => x.OriginalDefinition.ToDisplayString() is MicrosoftLogger or MicrosoftLogger + "<TCategoryName>"));

    // For a plain identifier/member argument, the candidate is its own name (e.g. "password" in LogInformation(password)).
    // For a message template literal, every {PlaceholderName} is a candidate, since that name is what a structured
    // logging backend (Application Insights, Seq, ...) actually indexes the value under, regardless of the C#
    // variable name of whatever value is passed positionally for it.
    internal static IEnumerable<string> CandidateNames(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier => [identifier.Identifier.ValueText],
            MemberAccessExpressionSyntax memberAccess => [memberAccess.Name.Identifier.ValueText],
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => ExtractPlaceholderNames(literal.Token.ValueText),
            _ => [],
        };

    internal static IEnumerable<string> ExtractPlaceholderNames(string template)
    {
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] != '{')
            {
                i++;
                continue;
            }

            if (i + 1 < template.Length && template[i + 1] == '{')
            {
                i += 2;
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                yield break;
            }

            var content = template.Substring(i + 1, close - i - 1);
            yield return content.Split(':')[0].Split(',')[0].TrimStart('@', '$');
            i = close + 1;
        }
    }
}
