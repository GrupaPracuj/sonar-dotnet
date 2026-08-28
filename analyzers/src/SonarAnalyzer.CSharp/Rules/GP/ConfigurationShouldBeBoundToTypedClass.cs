/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigurationShouldBeBoundToTypedClass : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0039";

    private const string MessageFormat = "Bind configuration to a typed class instead of reading it by key.";

    private const string ConfigurationInterface = "Microsoft.Extensions.Configuration.IConfiguration";
    private const string ServiceCollectionInterface = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string ApplicationBuilderInterface = "Microsoft.AspNetCore.Builder.IApplicationBuilder";
    private const string EndpointRouteBuilderInterface = "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
    private const string WebApplicationBuilderType = "Microsoft.AspNetCore.Builder.WebApplicationBuilder";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeElementAccess, SyntaxKind.ElementAccessExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // configuration["Orders:BaseUrl"]
    private static void AnalyzeElementAccess(SonarSyntaxNodeReportingContext context)
    {
        var elementAccess = (ElementAccessExpressionSyntax)context.Node;
        if (IsConfiguration(context.Model.GetTypeInfo(elementAccess.Expression).Type)
            && !IsCompositionRootContext(context.Model, elementAccess)
            && !IsInsideJuno(context.Model, elementAccess))
        {
            context.ReportIssue(Rule, elementAccess);
        }
    }

    // configuration.GetValue<int>("Orders:Timeout") - typed, but still one value looked up by key.
    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "GetValue" } method)
        {
            return;
        }

        // GetValue is an extension method on IConfiguration, so the receiver carries the type.
        if ((IsConfiguration(method.ReceiverType) || (method.Parameters.Length > 0 && IsConfiguration(method.Parameters[0].Type)))
            && !IsCompositionRootContext(context.Model, invocation)
            && !IsInsideJuno(context.Model, invocation))
        {
            context.ReportIssue(Rule, invocation);
        }
    }

    // GetSection(...) is not reported: it is how a section is selected before being bound with Get<T>()/Bind(...),
    // which is the pattern this rule steers towards.
    private static bool IsConfiguration(ITypeSymbol type) =>
        GpJunoTypes.Implements(type, ConfigurationInterface);

    private static bool IsInsideJuno(SemanticModel model, SyntaxNode node) =>
        model.GetEnclosingSymbol(node.SpanStart)?.ContainingNamespace?.ToDisplayString() is { } containingNamespace
        && (containingNamespace == "GP.Juno" || containingNamespace.StartsWith("GP.Juno.", StringComparison.Ordinal));

    private static bool IsCompositionRootContext(SemanticModel model, SyntaxNode node) =>
        ContainingExecutionContext(node) is { } context
        && IsCompositionRootContext(model.Compilation, context, new HashSet<IMethodSymbol>());

    private static bool IsServiceCollection(ITypeSymbol type) =>
        type?.ToDisplayString() == ServiceCollectionInterface;

    // Registering services and building the request pipeline are two halves of the same composition root. A method
    // that takes or returns IApplicationBuilder or IEndpointRouteBuilder - WebApplication implements both - is
    // configuring the host, not running application logic.
    private static bool IsPipelineBuilder(ITypeSymbol type) =>
        GpJunoTypes.Implements(type, ApplicationBuilderInterface)
        || GpJunoTypes.Implements(type, EndpointRouteBuilderInterface);

    private static bool IsCompositionRootContext(Compilation compilation, SyntaxNode context, HashSet<IMethodSymbol> visiting)
    {
        if (context is GlobalStatementSyntax)
        {
            return true;
        }

        if (context is AnonymousFunctionExpressionSyntax lambda)
        {
            return IsServiceRegistrationLambda(compilation, lambda);
        }

        var model = compilation.GetSemanticModel(context.SyntaxTree);
        return model.GetDeclaredSymbol(context) is IMethodSymbol method
            && (IsDirectCompositionRootMethod(compilation, method)
                || IsPrivateSetupHelperUsedOnlyFromCompositionRoot(compilation, method, visiting));
    }

    // The composition root is where configuration is intentionally converted into concrete dependencies. Reading a
    // single value while registering a framework service (for example an EF connection string) does not leak the
    // configuration bag into runtime application code.
    private static bool IsDirectCompositionRootMethod(Compilation compilation, IMethodSymbol method) =>
        IsTopLevelLocalFunction(method)
        || IsServiceCollection(method.ReturnType)
        || method.Parameters.Any(x => IsServiceCollection(x.Type))
        || IsPipelineBuilder(method.ReturnType)
        || method.Parameters.Any(x => IsPipelineBuilder(x.Type))
        || DeclaresServiceCollection(compilation, method)
        || UsesWebApplicationBuilderServices(compilation, method);

    // A loader that news up its own ServiceCollection is composing the graph just as much as a Startup that receives
    // one, so reading a value while registering a framework service there is not configuration leaking into
    // application code. Taking or returning the collection was not enough to recognise that shape.
    private static bool DeclaresServiceCollection(Compilation compilation, IMethodSymbol method) =>
        method.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .Any(x => x.DescendantNodes()
                .OfType<VariableDeclarationSyntax>()
                .Any(declaration => GpJunoTypes.Implements(
                    compilation.GetSemanticModel(x.SyntaxTree).GetTypeInfo(declaration.Type).Type,
                    ServiceCollectionInterface)));

    private static bool IsPrivateSetupHelperUsedOnlyFromCompositionRoot(Compilation compilation, IMethodSymbol method, HashSet<IMethodSymbol> visiting)
    {
        if (!IsLocalFunction(method) && method.DeclaredAccessibility != Accessibility.Private)
        {
            return false;
        }

        if (!visiting.Add(method))
        {
            return false;
        }

        try
        {
            var callSites = FindCallSites(compilation, method).ToList();
            return callSites.Count > 0 && callSites.All(x => IsCompositionRootContext(compilation, x, visiting));
        }
        finally
        {
            visiting.Remove(method);
        }
    }

    private static IEnumerable<SyntaxNode> FindCallSites(Compilation compilation, IMethodSymbol method)
    {
        var scopes = IsLocalFunction(method)
            ? method.DeclaringSyntaxReferences.Select(x => x.GetSyntax().Parent).WhereNotNull().Select(ContainingExecutionContext).WhereNotNull()
            : method.ContainingType.DeclaringSyntaxReferences.Select(x => x.GetSyntax());

        foreach (var scope in scopes.Distinct())
        {
            var model = compilation.GetSemanticModel(scope.SyntaxTree);
            foreach (var invocation in scope.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol candidate
                    && candidate.OriginalDefinition.Equals(method.OriginalDefinition)
                    && !Equals(model.GetEnclosingSymbol(invocation.SpanStart), method))
                {
                    yield return ContainingExecutionContext(invocation) ?? invocation;
                }
            }

            foreach (var argument in scope.DescendantNodesAndSelf().OfType<ArgumentSyntax>())
            {
                if (model.GetSymbolInfo(argument.Expression).Symbol is IMethodSymbol candidate
                    && candidate.OriginalDefinition.Equals(method.OriginalDefinition)
                    && !Equals(model.GetEnclosingSymbol(argument.SpanStart), method))
                {
                    yield return ContainingExecutionContext(argument) ?? argument;
                }
            }
        }
    }

    private static SyntaxNode ContainingExecutionContext(SyntaxNode node) =>
        node.AncestorsAndSelf().FirstOrDefault(x =>
            (x is GlobalStatementSyntax
            or AnonymousFunctionExpressionSyntax
            or AccessorDeclarationSyntax
            or BaseMethodDeclarationSyntax)
            || x.Kind() == SyntaxKindEx.LocalFunctionStatement);

    // Modern ASP.NET composition roots commonly start from top-level Program bootstrapping or a dedicated
    // registration method and then fan out into registration lambdas and small private setup helpers. Keep the
    // exemption tied to those shapes instead of suppressing arbitrary "Configure*" methods by name.
    private static bool IsServiceRegistrationLambda(Compilation compilation, AnonymousFunctionExpressionSyntax lambda)
    {
        var invocation = lambda.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(x => x.ArgumentList.Arguments.Any(a => a.DescendantNodesAndSelf().Contains(lambda)));
        var model = compilation.GetSemanticModel(lambda.SyntaxTree);
        if (invocation is not null)
        {
            return IsServiceRegistrationInvocation(model, invocation);
        }

        return lambda.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            && model.GetDeclaredSymbol(declarator) is ILocalSymbol local
            && lambda.SyntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Any(candidate =>
                IsServiceRegistrationInvocation(model, candidate)
                && candidate.ArgumentList.Arguments.Any(argument =>
                    argument.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(identifier =>
                        local.Equals(model.GetSymbolInfo(identifier).Symbol))));
    }

    private static bool IsServiceRegistrationInvocation(SemanticModel model, InvocationExpressionSyntax invocation) =>
        (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method && IsServiceRegistrationInvocation(method))
        || InvocationChainContainsServiceRegistration(model, invocation.Expression);

    private static bool InvocationChainContainsServiceRegistration(SemanticModel model, SyntaxNode expression) =>
        expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(x =>
            model.GetSymbolInfo(x).Symbol is IMethodSymbol method && IsServiceRegistrationInvocation(method))
        || expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Any(x => IsBuilderServicesAccess(model, x));

    private static bool IsServiceRegistrationInvocation(IMethodSymbol method) =>
        IsServiceCollection(method.ReceiverType)
        || (method.Parameters.FirstOrDefault() is { Type: { } firstParameterType } && IsServiceCollection(firstParameterType))
        || (method.ReducedFrom ?? method) is { Name: "ConfigureServices", ContainingType.Name: "HostingHostBuilderExtensions" } definition
           && definition.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.Hosting"
           && definition.Parameters.Any(x =>
               x.Type.TypeKind == TypeKind.Delegate
               && x.Type.GetMembers("Invoke").OfType<IMethodSymbol>().Any(invoke =>
                   invoke.Parameters.Any(parameter => IsServiceCollection(parameter.Type))));

    private static bool IsLocalFunction(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Any(x => x.GetSyntax().Kind() == SyntaxKindEx.LocalFunctionStatement);

    private static bool IsTopLevelLocalFunction(IMethodSymbol method) =>
        IsLocalFunction(method)
        && method.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .Any(x => x.Ancestors().OfType<GlobalStatementSyntax>().Any());

    private static bool UsesWebApplicationBuilderServices(Compilation compilation, IMethodSymbol method)
    {
        var builderParameters = method.Parameters.Where(x => x.Type.ToDisplayString() == WebApplicationBuilderType).ToList();
        if (builderParameters.Count == 0)
        {
            return false;
        }

        return method.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .Any(declaration =>
            {
                var model = compilation.GetSemanticModel(declaration.SyntaxTree);
                return declaration.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Any(access =>
                    access.Name.Identifier.ValueText == "Services"
                    && access.Expression is IdentifierNameSyntax identifier
                    && builderParameters.Any(parameter => parameter.Equals(model.GetSymbolInfo(identifier).Symbol)));
            });
    }

    private static bool IsBuilderServicesAccess(SemanticModel model, MemberAccessExpressionSyntax access) =>
        access.Name.Identifier.ValueText == "Services"
        && access.Expression is { } expression
        && model.GetTypeInfo(expression).Type?.ToDisplayString() == WebApplicationBuilderType;
}
