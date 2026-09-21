/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// AI007 / GP0145. Checks System.Text.Json calls that directly consume text from a known AI response.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AiStructuredOutputShouldDeserializeSafely : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0145";

    private const string MessageFormat = "{0}";
    private const string ResponseTextProperty = "Text";
    private const string JsonSerializerType = "System.Text.Json.JsonSerializer";
    private const string JsonSerializerOptionsType = "System.Text.Json.JsonSerializerOptions";
    private const string UnmappedMemberHandlingProperty = "UnmappedMemberHandling";
    private const string UnmappedMemberHandlingDisallowMember = "System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow";
    private const string ExpandoObjectType = "System.Dynamic.ExpandoObject";
    private const string JsonDocumentType = "System.Text.Json.JsonDocument";
    private const string MissingOptionsReason =
        "This AI response is deserialized without a JsonSerializerOptions that disallows unmapped members - pass one with UnmappedMemberHandling.Disallow.";
    private const string PermissiveOptionsReason =
        "The JsonSerializerOptions used to deserialize this AI response does not set UnmappedMemberHandling to Disallow.";
    private const string UntypedTargetReason =
        "This AI response is deserialized directly into a dynamic/loosely typed value instead of a typed model.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> DeserializeMethods = new(StringComparer.Ordinal) { "Deserialize", "DeserializeAsync" };
    private static readonly HashSet<string> JsonDocumentParseMethods = new(StringComparer.Ordinal) { "Parse", "ParseAsync" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !GpAi.TryFindArgument(invocation, method, IsSourceParameter, out var sourceExpression, out _)
            || sourceExpression is null
            || !IsKnownResponseText(context.Model, sourceExpression))
        {
            return;
        }

        if (IsDeserializeCall(method))
        {
            AnalyzeDeserializeCall(context, invocation, method);
        }
        else if (IsJsonDocumentParseCall(method))
        {
            context.ReportIssue(Rule, invocation, UntypedTargetReason);
        }
    }

    private static void AnalyzeDeserializeCall(SonarSyntaxNodeReportingContext context, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method.TypeArguments.Length == 1 && IsUnsafeDynamicType(method.TypeArguments[0]))
        {
            context.ReportIssue(Rule, invocation, UntypedTargetReason);
            return;
        }

        if (!GpAi.TryFindArgument(invocation, method, IsJsonSerializerOptionsParameter, out var optionsExpression, out _))
        {
            context.ReportIssue(Rule, invocation, MissingOptionsReason);
            return;
        }

        if (optionsExpression is null || GpAi.IsNullLiteral(optionsExpression))
        {
            context.ReportIssue(Rule, invocation, MissingOptionsReason);
            return;
        }

        if (!GpAi.TryResolveEffectivePropertyValue(context.Model, optionsExpression, invocation, UnmappedMemberHandlingProperty, out var value))
        {
            return; // Options come from elsewhere - not provable, accept.
        }

        if (value is null || !IsDisallowValue(context.Model, value))
        {
            context.ReportIssue(Rule, invocation, PermissiveOptionsReason);
        }
    }

    // The source parameter (the JSON text/bytes/reader being deserialized) is, on every overload this rule
    // recognizes - JsonSerializer.Deserialize(Async) and JsonDocument.Parse(Async) alike - the first declared
    // parameter. Matching by the method's own parameter identity (ordinal 0), rather than by the raw position of the
    // argument in the call, is what makes this correct even when a call reorders arguments through named arguments
    // (e.g. "Deserialize<T>(options: o, json: response.Text)").
    private static bool IsSourceParameter(IParameterSymbol parameter) => parameter.Ordinal == 0;

    private static bool IsDeserializeCall(IMethodSymbol method) =>
        DeserializeMethods.Contains(method.Name) && method.ContainingType?.OriginalDefinition.ToDisplayString() == JsonSerializerType;

    private static bool IsJsonDocumentParseCall(IMethodSymbol method) =>
        JsonDocumentParseMethods.Contains(method.Name) && method.ContainingType?.OriginalDefinition.ToDisplayString() == JsonDocumentType;

    private static bool IsJsonSerializerOptionsParameter(IParameterSymbol parameter) =>
        parameter.Type.OriginalDefinition.ToDisplayString() == JsonSerializerOptionsType;

    private static bool IsUnsafeDynamicType(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Dynamic || type.OriginalDefinition.ToDisplayString() == ExpandoObjectType;

    private static bool IsDisallowValue(SemanticModel model, ExpressionSyntax value) =>
        model.GetSymbolInfo(value.RemoveParentheses()).Symbol is IFieldSymbol field
        && field.ContainingType is { } containingType
        && $"{containingType.OriginalDefinition.ToDisplayString()}.{field.Name}" == UnmappedMemberHandlingDisallowMember;

    private static bool IsKnownResponseText(SemanticModel model, ExpressionSyntax expression) =>
        expression.RemoveParentheses() is MemberAccessExpressionSyntax { Name.Identifier.ValueText: { } name } memberAccess
        && name == ResponseTextProperty
        && model.GetTypeInfo(memberAccess.Expression).Type is { } receiverType
        && (GpAi.IsChatResponse(receiverType) || GpAi.IsAgentResponse(receiverType));
}
