/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ErrorResponsesShouldUseProblemDetails : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0110";

    private const string MessageFormat = "Use ProblemDetails for the response body of status {0} instead of '{1}'.";
    private const string ProblemDetailsType = "Microsoft.AspNetCore.Mvc.ProblemDetails";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly string[] MinimalApiMapMethods = ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete", "MapMethods"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeControllerAction, SyntaxKind.MethodDeclaration);
        context.RegisterNodeAction(AnalyzeController, SyntaxKind.ClassDeclaration);
        context.RegisterNodeAction(
            AnalyzeMinimalApiHandler,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression);
    }

    private static void AnalyzeControllerAction(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is not { } method
            || !GpOpenApiMetadata.IsOpenApiAction(method)
            || GpOpenApiMetadata.IsIgnored(method))
        {
            return;
        }

        var reportedStatuses = AnalyzeResponses(context, GpOpenApiMetadata.ReturnedInvocations(declaration));
        AnalyzeResponseAttributes(context, method.GetAttributes(), reportedStatuses);
    }

    private static void AnalyzeController(SonarSyntaxNodeReportingContext context)
    {
        if (context.Model.GetDeclaredSymbol(context.Node) is INamedTypeSymbol { IsCoreApiController: true } type)
        {
            AnalyzeResponseAttributes(context, type.GetAttributes(), []);
        }
    }

    private static void AnalyzeMinimalApiHandler(SonarSyntaxNodeReportingContext context)
    {
        var handler = (AnonymousFunctionExpressionSyntax)context.Node;
        if (GpMinimalApi.TryGetInlineHandler(
                handler.Body,
                context.Model,
                MinimalApiMapMethods,
                out var foundHandler,
                out _,
                out _,
                out _)
            && foundHandler == handler)
        {
            AnalyzeResponses(context, GpOpenApiMetadata.ReturnedInvocations(handler));
        }
    }

    private static HashSet<int> AnalyzeResponses(SonarSyntaxNodeReportingContext context, IEnumerable<InvocationExpressionSyntax> invocations)
    {
        var reportedStatuses = new HashSet<int>();
        foreach (var invocation in invocations)
        {
            var statusCode = ErrorStatusCode(context.Model, invocation);
            if (statusCode is >= 400 and <= 599
                && ResponsePayload(context.Model, invocation) is { } payload
                && context.Model.GetTypeInfo(payload).Type is { } payloadType
                && !IsProblemDetails(payloadType))
            {
                context.ReportIssue(Rule, invocation, statusCode.Value.ToString(), TypeDescription(payloadType));
                reportedStatuses.Add(statusCode.Value);
            }
        }

        return reportedStatuses;
    }

    private static void AnalyzeResponseAttributes(SonarSyntaxNodeReportingContext context,
                                                  ImmutableArray<AttributeData> attributes,
                                                  HashSet<int> reportedStatuses)
    {
        foreach (var attribute in attributes.Where(GpOpenApiMetadata.IsResponseAttribute))
        {
            var statusCode = GpOpenApiMetadata.ResponseStatusCode(attribute);
            var responseType = GpOpenApiMetadata.ResponseType(attribute);
            if (statusCode is >= 400 and <= 599
                && !reportedStatuses.Contains(statusCode.Value)
                && responseType is not null
                && !IsProblemDetails(responseType)
                && attribute.ApplicationSyntaxReference?.GetSyntax() is { } syntax)
            {
                context.ReportIssue(Rule, syntax, statusCode.Value.ToString(), TypeDescription(responseType));
            }
        }
    }

    private static int? ErrorStatusCode(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (GpOpenApiMetadata.ResponseStatusCode(model, invocation) is { } known)
        {
            return known;
        }

        if (!GpOpenApiMetadata.TryGetResponseMethod(model, invocation, out var method))
        {
            return null;
        }

        var lookup = new CSharpMethodParameterLookup(invocation, method);
        return lookup.TryGetSyntax("statusCode", out var arguments)
               && arguments.Length == 1
               && arguments[0] is ExpressionSyntax expression
               && model.GetConstantValue(expression) is { HasValue: true, Value: int statusCode }
            ? statusCode
            : null;
    }

    private static ExpressionSyntax ResponsePayload(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (!GpOpenApiMetadata.TryGetResponseMethod(model, invocation, out var method))
        {
            return null;
        }

        var lookup = new CSharpMethodParameterLookup(invocation, method);
        // These are the payload parameter names used by the supported MVC and Minimal API response factories.
        return new[] { "value", "data", "error" }
            .SelectMany(x => lookup.TryGetSyntax(x, out var arguments)
                ? arguments.AsEnumerable()
                : Enumerable.Empty<SyntaxNode>())
            .OfType<ExpressionSyntax>()
            .FirstOrDefault();
    }

    private static bool IsProblemDetails(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == ProblemDetailsType)
            {
                return true;
            }
        }

        return false;
    }

    private static string TypeDescription(ITypeSymbol type) =>
        type.IsAnonymousType ? "an anonymous type" : type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
}
