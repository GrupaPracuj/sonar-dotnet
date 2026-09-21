/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// AI008 / GP0146. Reports known raw AI content passed to logging APIs recognized by GpLoggingHelper.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AiDataShouldNotBeLoggedRaw : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0146";

    private const string MessageFormat = "Do not log this raw AI {0} directly - log only derived metadata (model id, token counts, duration, finish reason, request id).";
    private const string ResponseTextProperty = "Text";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!GpLoggingHelper.IsLoggingCall(context.Model, invocation))
        {
            return;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (FindOffending(context.Model, argument.Expression) is { } offending)
            {
                context.ReportIssue(Rule, offending.Location, offending.Kind);
            }
        }
    }

    // Recurses into the two shapes that commonly wrap a piece of logged content instead of passing it bare: a
    // string interpolation hole ($"... {response} ...") and a '+' string concatenation ("... " + response.Text).
    // Reporting at the exact offending sub-expression (not the whole argument) keeps the finding precise when it is
    // buried a few '+' operands or interpolation holes deep.
    private static (string Kind, ExpressionSyntax Location)? FindOffending(SemanticModel model, ExpressionSyntax expression)
    {
        var expr = (ExpressionSyntax)(expression.RemoveParentheses() ?? expression);
        switch (expr)
        {
            case InterpolatedStringExpressionSyntax interpolated:
                foreach (var content in interpolated.Contents)
                {
                    if (content is InterpolationSyntax interpolation && FindOffending(model, interpolation.Expression) is { } foundInHole)
                    {
                        return foundInHole;
                    }
                }

                return null;
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } binary:
                return FindOffending(model, binary.Left) ?? FindOffending(model, binary.Right);
            default:
                return FindOffendingLeaf(model, expr);
        }
    }

    private static (string Kind, ExpressionSyntax Location)? FindOffendingLeaf(SemanticModel model, ExpressionSyntax expr)
    {
        if (IsResponseTextAccess(model, expr))
        {
            return ("response text", expr);
        }

        if (model.GetTypeInfo(expr).Type is { } type && RawContentKind(type) is { } kind)
        {
            return (kind, expr);
        }

        return null;
    }

    private static bool IsResponseTextAccess(SemanticModel model, ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: { } name } memberAccess
        && name == ResponseTextProperty
        && model.GetTypeInfo(memberAccess.Expression).Type is { } receiverType
        && (GpAi.IsChatResponse(receiverType) || GpAi.IsAgentResponse(receiverType));

    private static string RawContentKind(ITypeSymbol type)
    {
        if (GpAi.IsChatMessage(type) || IsCollectionOfChatMessages(type))
        {
            return "chat message";
        }

        if (GpAi.IsChatResponse(type))
        {
            return "chat response";
        }

        if (GpAi.IsAgentResponse(type))
        {
            return "agent response";
        }

        if (GpAi.IsUsageContent(type))
        {
            return "usage content";
        }

        return null;
    }

    private static bool IsCollectionOfChatMessages(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return GpAi.IsChatMessage(array.ElementType);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        return named.AllInterfaces.Append(named).Any(x =>
            x.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
            && x.TypeArguments.Length == 1
            && GpAi.IsChatMessage(x.TypeArguments[0]));
    }
}
