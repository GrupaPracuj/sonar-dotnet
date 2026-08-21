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

    private const string MessageFormat = "Method '{0}' has neither [Authorize] nor [AllowAnonymous]; explicitly declare its access policy.";
    private const string AllowAnonymousAttribute = "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";
    private const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string AuthorizeAttribute = "Microsoft.AspNetCore.Authorization.AuthorizeAttribute";
    private const string AuthorizationOptions = "Microsoft.AspNetCore.Authorization.AuthorizationOptions";
    private const string AuthorizationPolicyBuilder = "Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder";
    private const string AuthorizationEndpointConventionBuilderExtensions = "Microsoft.AspNetCore.Builder.AuthorizationEndpointConventionBuilderExtensions";
    private const string AuthorizeFilter = "Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter";
    private const string ControllerEndpointRouteBuilderExtensions = "Microsoft.AspNetCore.Builder.ControllerEndpointRouteBuilderExtensions";
    private const string FilterCollection = "Microsoft.AspNetCore.Mvc.Filters.FilterCollection";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            if (!HasDirectGlobalProtection(start.Compilation))
            {
                start.RegisterNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
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

    private static bool HasDirectGlobalProtection(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree);
            if (root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(x => IsAuthenticatedFallbackPolicy(model, x))
                || root.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(x =>
                    AddsGlobalAuthorizeFilter(model, x) || RequiresAuthorizationForControllers(model, x)))
            {
                return true;
            }
        }
        return false;
    }

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
