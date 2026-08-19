/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EndpointsShouldNotExposeExceptionDetails : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0071";

    private const string MessageFormat = "Do not put '{0}' in a response - return a ProblemDetails without internal details.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly string[] MinimalApiMapMethods = ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete", "MapMethods"];

    private static readonly HashSet<string> ExceptionDetailMembers = new(StringComparer.Ordinal)
    {
        "Message",
        "StackTrace",
        "Source",
        "InnerException",
        "ToString",
    };

    // Methods that turn a value into the HTTP response body.
    private static readonly HashSet<string> ResponseProducingMethods = new(StringComparer.Ordinal)
    {
        "Ok",
        "BadRequest",
        "Unauthorized",
        "Content",
        "Json",
        "Problem",
        "StatusCode",
        "UnprocessableEntity",
        "Conflict",
        "NotFound",
        "ValidationProblem",
        "Created",
        "CreatedAtAction",
        "CreatedAtRoute",
        "Accepted",
        "AcceptedAtAction",
        "AcceptedAtRoute",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);

    private static void AnalyzeMemberAccess(SonarSyntaxNodeReportingContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (!ExceptionDetailMembers.Contains(memberAccess.Name.Identifier.ValueText)
            || IsReceiverOfMoreSpecificExceptionDetail(memberAccess)
            || context.Model.GetTypeInfo(memberAccess.Expression).Type is not { } receiver
            || !IsException(receiver)
            || !FlowsIntoControllerResponse(memberAccess, context.Model)
               && !FlowsDirectlyIntoMinimalApiResponse(memberAccess, context.Model))
        {
            return;
        }

        context.ReportIssue(Rule, memberAccess, $"{receiver.Name}.{memberAccess.Name.Identifier.ValueText}");
    }

    private static bool IsException(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Is(KnownType.System_Exception))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FlowsIntoControllerResponse(MemberAccessExpressionSyntax memberAccess, SemanticModel model) =>
        model.GetEnclosingSymbol(memberAccess.SpanStart) is IMethodSymbol enclosing
        && enclosing.IsControllerActionMethod
        && FlowsIntoTheResponse(memberAccess, model, x => IsMvcResponseFactory(x, model), null);

    private static bool FlowsDirectlyIntoMinimalApiResponse(MemberAccessExpressionSyntax memberAccess, SemanticModel model)
    {
        return GpMinimalApi.TryGetInlineHandler(memberAccess, model, MinimalApiMapMethods, out var handler, out _, out _, out _)
               && FlowsIntoTheResponse(memberAccess, model, x => GpMinimalApi.TryGetResultMethod(model, x, out _), handler);
    }

    private static bool IsReceiverOfMoreSpecificExceptionDetail(MemberAccessExpressionSyntax memberAccess) =>
        memberAccess.Parent is MemberAccessExpressionSyntax { Expression: var expression, Name.Identifier.ValueText: var name }
        && expression == memberAccess
        && ExceptionDetailMembers.Contains(name);

    private static bool IsMvcResponseFactory(InvocationExpressionSyntax invocation, SemanticModel model) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
        && ResponseProducingMethods.Contains(method.Name)
        && method.ContainingType?.ToDisplayString() is "Microsoft.AspNetCore.Mvc.ControllerBase" or "Microsoft.AspNetCore.Mvc.Controller";

    // Follow only syntax that preserves the exposed value. Calls such as Message.ToUpper() and aliases deliberately
    // stop the walk: recognizing whether arbitrary transformations sanitize the value requires data-flow analysis.
    private static bool FlowsIntoTheResponse(MemberAccessExpressionSyntax memberAccess,
                                             SemanticModel model,
                                             Func<InvocationExpressionSyntax, bool> isResponseFactory,
                                             AnonymousFunctionExpressionSyntax minimalApiHandler)
    {
        SyntaxNode current = memberAccess.Name.Identifier.ValueText == "ToString"
                             && memberAccess.Parent is InvocationExpressionSyntax { Expression: var expression } toStringInvocation
                             && expression == memberAccess
            ? toStringInvocation
            : memberAccess;

        while (current.Parent is { } parent)
        {
            switch (parent)
            {
                case ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } } when isResponseFactory(invocation):
                    return true;
                case ReturnStatementSyntax or ArrowExpressionClauseSyntax:
                    return true;
                case AnonymousFunctionExpressionSyntax handler when handler == minimalApiHandler && handler.Body == current:
                    return true;
            }

            if (!IsValuePreservingWrapper(parent, current, model))
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    private static bool IsValuePreservingWrapper(SyntaxNode parent, SyntaxNode current, SemanticModel model) =>
        parent switch
        {
            ParenthesizedExpressionSyntax { Expression: var expression } => expression == current,
            CastExpressionSyntax { Expression: var expression } => expression == current,
            ConditionalExpressionSyntax conditional => conditional.WhenTrue == current || conditional.WhenFalse == current,
            BinaryExpressionSyntax binary when binary.Left == current || binary.Right == current =>
                model.GetTypeInfo(binary).Type?.SpecialType == SpecialType.System_String,
            InterpolationSyntax { Expression: var expression } => expression == current,
            InterpolatedStringExpressionSyntax => true,
            AnonymousObjectMemberDeclaratorSyntax { Expression: var expression } => expression == current,
            AnonymousObjectCreationExpressionSyntax => true,
            InitializerExpressionSyntax => true,
            ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax => true,
            _ => false,
        };
}
