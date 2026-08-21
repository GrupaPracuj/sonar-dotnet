/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ServiceDiscoveryShouldGoThroughJuno : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0047";

    private const string MessageFormat = "Resolve the service through Juno instead of querying '{0}' directly.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> AgentDiscoveryMethods = new(StringComparer.Ordinal)
    {
        "CheckDeregister",
        "CheckRegister",
        "ServiceDeregister",
        "ServiceRegister",
    };

    private static readonly HashSet<string> DiscoveryRegistrationTypes = new(StringComparer.Ordinal)
    {
        "Consul.AgentCheckRegistration",
        "Consul.AgentServiceCheck",
        "Consul.AgentServiceRegistration",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (IsInsideDiscoveryProvider(context)
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !ShouldReportInvocation(context.Model, invocation, method))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.ContainingType.Name);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (!IsInsideDiscoveryProvider(context)
            && ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && DiscoveryRegistrationTypes.Contains(type.ToDisplayString())
            && !IsPartOfReportedDiscoveryInvocation(context))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    private static bool IsPartOfReportedDiscoveryInvocation(SonarSyntaxNodeReportingContext context) =>
        context.Node.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Select(x => context.Model.GetSymbolInfo(x).Symbol)
            .OfType<IMethodSymbol>()
            .Any(ReportsObjectCreation);

    private static bool ShouldReportInvocation(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        ReportsObjectCreation(method)
        || (IsCatalogOrHealthQuery(method) && IsUsedForOutboundServiceCall(model, invocation));

    private static bool ReportsObjectCreation(IMethodSymbol method) =>
        GpJunoTypes.Implements(method.ContainingType, "Consul.IAgentEndpoint")
        && AgentDiscoveryMethods.Contains(method.Name);

    private static bool IsCatalogOrHealthQuery(IMethodSymbol method) =>
        GpJunoTypes.Implements(method.ContainingType, "Consul.ICatalogEndpoint")
        || GpJunoTypes.Implements(method.ContainingType, "Consul.IHealthEndpoint");

    private static bool IsUsedForOutboundServiceCall(SemanticModel model, InvocationExpressionSyntax discoveryInvocation)
    {
        if (ContainingExecutableBody(discoveryInvocation) is not { } body)
        {
            return false;
        }

        var assignments = CollectAssignments(model, body);
        var discoveryConfiguredTargets = DiscoveryConfiguredTargets(model, body, discoveryInvocation, assignments);
        return body.DescendantNodesAndSelf(DoesNotBelongToNestedFunction)
            .OfType<InvocationExpressionSyntax>()
            .Where(x => x != discoveryInvocation)
            .Any(x => IsOutboundServiceCall(model, x)
                      && (x.ArgumentList.Arguments.Any(argument =>
                              DependsOnDiscovery(model, argument.Expression, discoveryInvocation, assignments, new HashSet<ILocalSymbol>()))
                          || UsesDiscoveryConfiguredTarget(model, x, discoveryConfiguredTargets)));
    }

    private static HashSet<ISymbol> DiscoveryConfiguredTargets(
        SemanticModel model,
        SyntaxNode body,
        InvocationExpressionSyntax discoveryInvocation,
        Dictionary<ILocalSymbol, List<ExpressionSyntax>> assignments)
    {
        var targets = new HashSet<ISymbol>();
        foreach (var assignment in body.DescendantNodesAndSelf(DoesNotBelongToNestedFunction).OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Kind() == SyntaxKind.SimpleAssignmentExpression
                && assignment.Left is MemberAccessExpressionSyntax memberAccess
                && IsOutboundAddressProperty(model.GetSymbolInfo(memberAccess.Name).Symbol as IPropertySymbol)
                && model.GetSymbolInfo(memberAccess.Expression).Symbol is { } target
                && DependsOnDiscovery(model, assignment.Right, discoveryInvocation, assignments, new HashSet<ILocalSymbol>()))
            {
                targets.Add(target);
            }
        }

        return targets;
    }

    private static bool IsOutboundAddressProperty(IPropertySymbol property) =>
        property?.Name == "BaseAddress"
            && property.ContainingType.Is(KnownType.System_Net_Http_HttpClient)
        || property?.Name == "RequestUri"
            && property.ContainingType.ToDisplayString() == "System.Net.Http.HttpRequestMessage";

    private static bool UsesDiscoveryConfiguredTarget(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        HashSet<ISymbol> targets) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && targets.Contains(model.GetSymbolInfo(memberAccess.Expression).Symbol)
        || invocation.ArgumentList.Arguments.Any(argument =>
            argument.Expression.DescendantNodesAndSelf().Any(node =>
                targets.Contains(model.GetSymbolInfo(node).Symbol)));

    private static Dictionary<ILocalSymbol, List<ExpressionSyntax>> CollectAssignments(SemanticModel model, SyntaxNode body)
    {
        var assignments = new Dictionary<ILocalSymbol, List<ExpressionSyntax>>();
        foreach (var declarator in body.DescendantNodesAndSelf(DoesNotBelongToNestedFunction).OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer?.Value is { } initializer
                && model.GetDeclaredSymbol(declarator) is ILocalSymbol local)
            {
                AddAssignment(assignments, local, initializer);
            }
        }

        foreach (var assignment in body.DescendantNodesAndSelf(DoesNotBelongToNestedFunction).OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Kind() == SyntaxKind.SimpleAssignmentExpression
                && model.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol local)
            {
                AddAssignment(assignments, local, assignment.Right);
            }
        }

        return assignments;
    }

    private static void AddAssignment(Dictionary<ILocalSymbol, List<ExpressionSyntax>> assignments, ILocalSymbol local, ExpressionSyntax expression)
    {
        if (!assignments.TryGetValue(local, out var expressions))
        {
            expressions = [];
            assignments.Add(local, expressions);
        }

        expressions.Add(expression);
    }

    private static bool DependsOnDiscovery(
        SemanticModel model,
        ExpressionSyntax expression,
        InvocationExpressionSyntax discoveryInvocation,
        Dictionary<ILocalSymbol, List<ExpressionSyntax>> assignments,
        HashSet<ILocalSymbol> visiting)
    {
        if (expression.DescendantNodesAndSelf().Any(x => x == discoveryInvocation))
        {
            return true;
        }

        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (model.GetSymbolInfo(identifier).Symbol is ILocalSymbol local
                && DependsOnDiscovery(model, local, discoveryInvocation, assignments, visiting))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DependsOnDiscovery(
        SemanticModel model,
        ILocalSymbol local,
        InvocationExpressionSyntax discoveryInvocation,
        Dictionary<ILocalSymbol, List<ExpressionSyntax>> assignments,
        HashSet<ILocalSymbol> visiting)
    {
        if (!visiting.Add(local))
        {
            return false;
        }

        try
        {
            return assignments.TryGetValue(local, out var expressions)
                && expressions.Any(x => DependsOnDiscovery(model, x, discoveryInvocation, assignments, visiting));
        }
        finally
        {
            visiting.Remove(local);
        }
    }

    private static bool IsOutboundServiceCall(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        var containingType = method.ContainingType.ToDisplayString();
        return containingType is "System.Net.Http.HttpClient" or "System.Net.Http.HttpMessageInvoker"
            || (OutboundMethodNames.Contains(method.Name)
                && (method.ContainingType.Name.EndsWith("Client", StringComparison.Ordinal)
                    || method.ContainingType.Name.EndsWith("Invoker", StringComparison.Ordinal)
                    || method.ContainingType.Name.EndsWith("Requester", StringComparison.Ordinal))
                && method.Parameters.Any(x => OutboundAddressParameterNames.Contains(x.Name)));
    }

    private static SyntaxNode ContainingExecutableBody(SyntaxNode node) =>
        node.AncestorsAndSelf().Select(ExecutableBody).FirstOrDefault(x => x is not null);

    private static SyntaxNode ExecutableBody(SyntaxNode node) =>
        node switch
        {
            AnonymousFunctionExpressionSyntax { Body: { } body } => body,
            AccessorDeclarationSyntax { Body: { } body } => body,
            AccessorDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
            MethodDeclarationSyntax { Body: { } body } => body,
            MethodDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
            GlobalStatementSyntax { Statement: { } statement } => statement,
            _ => null,
        };

    private static bool DoesNotBelongToNestedFunction(SyntaxNode node) =>
        node is not AnonymousFunctionExpressionSyntax
        && node.Kind() != SyntaxKindEx.LocalFunctionStatement;

    // Juno and dedicated discovery-provider assemblies are the layers that are supposed to wrap Consul. The latter
    // are identified by their implementation of Akka's discovery abstraction, not by a project or namespace name.
    private static bool IsInsideDiscoveryProvider(SonarSyntaxNodeReportingContext context) =>
        IsInsideJuno(context)
        || ContainsAkkaDiscoveryProvider(context.Compilation.Assembly.GlobalNamespace);

    private static bool IsInsideJuno(SonarSyntaxNodeReportingContext context) =>
        context.Model.GetEnclosingSymbol(context.Node.SpanStart)?.ContainingNamespace?.ToDisplayString() is { } containingNamespace
        && (containingNamespace == "GP.Juno" || containingNamespace.StartsWith("GP.Juno.", StringComparison.Ordinal));

    private static bool ContainsAkkaDiscoveryProvider(INamespaceSymbol root) =>
        root.GetTypeMembers().Any(IsAkkaDiscoveryProvider)
        || root.GetNamespaceMembers().Any(ContainsAkkaDiscoveryProvider);

    private static bool IsAkkaDiscoveryProvider(INamedTypeSymbol type) =>
        type.ToDisplayString() != "Akka.Cluster.Discovery.DiscoveryService"
        && GpJunoTypes.DerivesFrom(type, "Akka.Cluster.Discovery.DiscoveryService")
        || type.GetTypeMembers().Any(IsAkkaDiscoveryProvider);

    private static readonly HashSet<string> OutboundMethodNames = new(StringComparer.Ordinal)
    {
        "Delete",
        "DeleteAsync",
        "Get",
        "GetAsync",
        "GetStringAsync",
        "Patch",
        "PatchAsync",
        "Post",
        "PostAsync",
        "Put",
        "PutAsync",
        "Send",
        "SendAsync",
    };

    private static readonly HashSet<string> OutboundAddressParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "address",
        "baseAddress",
        "endpoint",
        "host",
        "requestUri",
        "uri",
        "url",
    };
}
