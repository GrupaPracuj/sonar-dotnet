/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ActionShouldDeclareAccessPolicy : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0020";

    private const string MessageFormat = "Endpoint '{0}' has neither authorization nor anonymous access explicitly declared.";
    private const string AllowAnonymousAttribute = "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";
    private const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string AuthorizeAttribute = "Microsoft.AspNetCore.Authorization.AuthorizeAttribute";
    private const string AuthorizationOptions = "Microsoft.AspNetCore.Authorization.AuthorizationOptions";
    private const string AuthorizationPolicyBuilder = "Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder";
    private const string AuthorizationEndpointConventionBuilderExtensions = "Microsoft.AspNetCore.Builder.AuthorizationEndpointConventionBuilderExtensions";
    private const string AuthorizeFilter = "Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter";
    private const string ControllerEndpointRouteBuilderExtensions = "Microsoft.AspNetCore.Builder.ControllerEndpointRouteBuilderExtensions";
    private const string FilterCollection = "Microsoft.AspNetCore.Mvc.Filters.FilterCollection";

    private static readonly string[] MinimalApiMapMethods = ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete", "MapMethods"];

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var hasFallbackPolicy = HasAuthenticatedFallbackPolicy(start.Compilation);
            if (!hasFallbackPolicy && !HasMvcOnlyGlobalProtection(start.Compilation))
            {
                start.RegisterNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
            }
            if (!hasFallbackPolicy)
            {
                start.RegisterNodeAction(AnalyzeMinimalApiEndpoint, SyntaxKind.InvocationExpression);
            }
        });

    private static void AnalyzeClass(SonarSyntaxNodeReportingContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(classDeclaration) is not { } type)
        {
            return;
        }

        var actionMethods = type.GetMembers().OfType<IMethodSymbol>().Where(x => x.IsControllerActionMethod).ToList();
        if (DeclaresAccessPolicyForWholeType(type)
            || (!IsApiController(type) && !actionMethods.Any(x => HasAttribute(x, AuthorizeAttribute))))
        {
            // Classic MVC keeps the established mixed-controller behavior; API controllers require an explicit default.
            return;
        }

        foreach (var method in actionMethods.Where(x => !HasAttribute(x, AuthorizeAttribute) && !HasAttribute(x, AllowAnonymousAttribute)))
        {
            if (method.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault(x => x.Parent == classDeclaration) is { } methodDeclaration)
            {
                context.ReportIssue(Rule, methodDeclaration.Identifier, method.Name);
            }
        }
    }

    private static void AnalyzeMinimalApiEndpoint(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpMinimalApi.TryGetMapMethod(invocation, context.Model, MinimalApiMapMethods, out var mapMethod, out var routeTemplate)
            && !FluentChainDeclaresAccessPolicy(invocation, context.Model)
            && !ReceiverDeclaresAccessPolicy(invocation, mapMethod, context.Model))
        {
            context.ReportIssue(Rule, invocation, routeTemplate ?? mapMethod.Name);
        }
    }

    private static bool DeclaresAccessPolicyForWholeType(INamedTypeSymbol type) =>
        type.AttributesWithInherited.Any(x => IsAttribute(x, AuthorizeAttribute) || IsAttribute(x, AllowAnonymousAttribute));

    private static bool IsApiController(INamedTypeSymbol type) =>
        type.AttributesWithInherited.Any(x => IsAttribute(x, ApiControllerAttribute))
        || type.ContainingAssembly.GetAttributes().Any(x => IsAttribute(x, ApiControllerAttribute));

    private static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol.AttributesWithInherited.Any(x => IsAttribute(x, metadataName));

    private static bool IsAttribute(AttributeData attribute, string metadataName) =>
        DerivesFrom(attribute.AttributeClass, metadataName);

    private static bool DerivesFrom(INamedTypeSymbol type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == metadataName)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasAuthenticatedFallbackPolicy(Compilation compilation) =>
        CompilationContains(compilation, (root, model) =>
            root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(x => IsAuthenticatedFallbackPolicy(model, x)));

    private static bool HasMvcOnlyGlobalProtection(Compilation compilation) =>
        CompilationContains(compilation, (root, model) =>
            root.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(x =>
                AddsGlobalAuthorizeFilter(model, x) || RequiresAuthorizationForControllers(model, x)));

    private static bool CompilationContains(Compilation compilation, Func<SyntaxNode, SemanticModel, bool> predicate)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree);
            if (predicate(root, model))
            {
                return true;
            }
        }
        return false;
    }

    private static bool FluentChainDeclaresAccessPolicy(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        SyntaxNode current = invocation;
        while (current.Parent is MemberAccessExpressionSyntax { Expression: var receiver } memberAccess
               && receiver == current
               && memberAccess.Parent is InvocationExpressionSyntax chainedInvocation)
        {
            if (IsAccessPolicyInvocation(chainedInvocation, model))
            {
                return true;
            }
            current = chainedInvocation;
        }
        return false;
    }

    private static bool ReceiverDeclaresAccessPolicy(InvocationExpressionSyntax mapInvocation, IMethodSymbol mapMethod, SemanticModel model)
    {
        var receiver = ReceiverExpression(mapInvocation, mapMethod);
        if (InvocationChainDeclaresAccessPolicy(receiver, model))
        {
            return true;
        }

        return receiver is not null
               && model.GetSymbolInfo(receiver.RemoveParentheses()).Symbol is ILocalSymbol local
               && StableLocalRouteGroupDeclaresAccessPolicy(local, mapInvocation, model);
    }

    private static bool InvocationChainDeclaresAccessPolicy(ExpressionSyntax expression, SemanticModel model)
    {
        expression = (ExpressionSyntax)expression?.RemoveParentheses();
        if (expression is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        if (IsAccessPolicyInvocation(invocation, model))
        {
            return true;
        }

        return model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
               && InvocationChainDeclaresAccessPolicy(ReceiverExpression(invocation, method), model);
    }

    private static bool StableLocalRouteGroupDeclaresAccessPolicy(ILocalSymbol local,
                                                                   InvocationExpressionSyntax mapInvocation,
                                                                   SemanticModel model)
    {
        if (local.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<VariableDeclaratorSyntax>().SingleOrDefault() is not { } declaration
            || declaration.Initializer?.Value is not { } initializer
            || !ContainsMapGroupInvocation(initializer, model)
            || mapInvocation.FirstAncestorOrSelf<BlockSyntax>() is not { } block
            || declaration.FirstAncestorOrSelf<BlockSyntax>() != block
            || IsReassignedBetween(local, declaration.Span.End, mapInvocation.SpanStart, block, model))
        {
            return false;
        }

        return InvocationChainDeclaresAccessPolicy(initializer, model)
               || block.Statements
                   .OfType<ExpressionStatementSyntax>()
                   .SelectMany(x => x.Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
                   .Any(x => x.SpanStart > declaration.Span.End
                             && x.SpanStart < mapInvocation.SpanStart
                             && IsAccessPolicyInvocation(x, model)
                             && AccessPolicyTargetsLocal(x, local, model));
    }

    private static bool AccessPolicyTargetsLocal(InvocationExpressionSyntax invocation, ILocalSymbol local, SemanticModel model) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
        && ReceiverExpression(invocation, method) is { } receiverExpression
        && model.GetSymbolInfo(receiverExpression.RemoveParentheses()).Symbol is { } receiver
        && receiver.Equals(local);

    private static bool ContainsMapGroupInvocation(ExpressionSyntax expression, SemanticModel model) =>
        expression.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(x => GpMinimalApi.TryGetMapMethod(x, model, ["MapGroup"], out _, out _));

    private static bool IsReassignedBetween(ILocalSymbol local,
                                            int start,
                                            int end,
                                            BlockSyntax block,
                                            SemanticModel model) =>
        block.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(x => x.SpanStart > start
                      && x.SpanStart < end
                      && model.GetSymbolInfo(x.Left).Symbol is { } assigned
                      && assigned.Equals(local));

    private static bool IsAccessPolicyInvocation(InvocationExpressionSyntax invocation, SemanticModel model) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol
        {
            Name: "RequireAuthorization" or "AllowAnonymous",
            ContainingType: { } containingType,
        }
        && containingType.ToDisplayString() == AuthorizationEndpointConventionBuilderExtensions;

    private static ExpressionSyntax ReceiverExpression(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.ReducedFrom is not null
            ? (invocation.Expression as MemberAccessExpressionSyntax)?.Expression
            : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;

    private static bool IsAuthenticatedFallbackPolicy(SemanticModel model, AssignmentExpressionSyntax assignment) =>
        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
        && model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol
        {
            Name: "FallbackPolicy",
            ContainingType: { } containingType,
        }
        && containingType.ToDisplayString() == AuthorizationOptions
        && assignment.Right.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(x =>
            model.GetSymbolInfo(x).Symbol is IMethodSymbol
            {
                Name: "RequireAuthenticatedUser",
                ContainingType: { } builderType,
            }
            && builderType.ToDisplayString() == AuthorizationPolicyBuilder);

    private static bool AddsGlobalAuthorizeFilter(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol
        {
            Name: "Add",
        }
        && invocation.Expression is MemberAccessExpressionSyntax memberAccess
        && model.GetTypeInfo(memberAccess.Expression).Type?.ToDisplayString() == FilterCollection
        && invocation.ArgumentList.Arguments.Count == 1
        && IsStableAuthorizeFilter(invocation.ArgumentList.Arguments.First().Expression, invocation, model);

    private static bool IsStableAuthorizeFilter(ExpressionSyntax expression, InvocationExpressionSyntax addInvocation, SemanticModel model)
    {
        expression = (ExpressionSyntax)expression.RemoveParentheses();
        if (expression is ObjectCreationExpressionSyntax creation)
        {
            return model.GetTypeInfo(creation).Type?.ToDisplayString() == AuthorizeFilter;
        }

        if (model.GetSymbolInfo(expression).Symbol is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<VariableDeclaratorSyntax>().SingleOrDefault() is not { } declaration
            || declaration.Initializer?.Value.RemoveParentheses() is not ObjectCreationExpressionSyntax initializer
            || model.GetTypeInfo(initializer).Type?.ToDisplayString() != AuthorizeFilter)
        {
            return false;
        }

        return addInvocation.FirstAncestorOrSelf<BlockSyntax>() is { } block
            && !block.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Any(x => x.SpanStart > declaration.Span.End
                          && x.SpanStart < addInvocation.SpanStart
                          && model.GetSymbolInfo(x.Left).Symbol is { } assigned
                          && assigned.Equals(local));
    }

    private static bool RequiresAuthorizationForControllers(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
            {
                Name: "RequireAuthorization",
                ContainingType: { } containingType,
            } method
            || containingType.ToDisplayString() != AuthorizationEndpointConventionBuilderExtensions)
        {
            return false;
        }

        var builderExpression = method.ReducedFrom is not null
            ? (invocation.Expression as MemberAccessExpressionSyntax)?.Expression
            : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        return builderExpression is InvocationExpressionSyntax mapControllers
            && model.GetSymbolInfo(mapControllers).Symbol is IMethodSymbol
            {
                Name: "MapControllers",
                ContainingType: { } mapContainingType,
            }
            && mapContainingType.ToDisplayString() == ControllerEndpointRouteBuilderExtensions;
    }
}
