/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GetEndpointsShouldNotHaveSideEffects : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0046";

    private const string MessageFormat = "A GET endpoint should not change state - '{0}' makes this endpoint unsafe to retry, prefetch or cache.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> PersistenceMethods = new(StringComparer.Ordinal)
    {
        "SaveChanges",
        "SaveChangesAsync",
        "Add",
        "AddAsync",
        "AddRange",
        "AddRangeAsync",
        "Update",
        "UpdateRange",
        "Remove",
        "RemoveRange",
        "ExecuteDelete",
        "ExecuteDeleteAsync",
        "ExecuteUpdate",
        "ExecuteUpdateAsync",
    };

    private static readonly HashSet<string> MessagingMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
        "RespondAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !IsStateChanging(method)
            || !IsInGetEndpoint(invocation, context.Model))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.Name);
    }

    // Controllers delegate: the write that makes a GET unsafe is almost never in the action itself but in the handler,
    // service or repository it calls. The call graph is the one the loop rules already use, rooted at GET actions here.
    // It is built per compilation, so a handler living in its own assembly stays out of reach.
    private static bool IsInGetEndpoint(InvocationExpressionSyntax invocation, SemanticModel model) =>
        (model.GetEnclosingSymbol(invocation.SpanStart) is IMethodSymbol enclosing && IsHttpGetAction(enclosing))
        || GpMinimalApi.TryGetInlineHandler(invocation, model, "MapGet", out _, out _, out _, out _)
        || GpSynchronousApiReachability.IsReachableFromGet(model, invocation);

    private static bool IsStateChanging(IMethodSymbol method) =>
        (PersistenceMethods.Contains(method.Name) && IsEntityFrameworkTarget(method.ContainingType))
        || (MessagingMethods.Contains(method.Name) && IsMessagingTarget(method));

    // Add/Update/Remove are common names, so they only count on a DbContext or a DbSet - not on any list.
    private static bool IsEntityFrameworkTarget(ITypeSymbol type) =>
        type is not null
        && (GpJunoTypes.DerivesFrom(type, "Microsoft.EntityFrameworkCore.DbContext")
            || GpJunoTypes.DerivesFrom(type, "System.Data.Entity.DbContext")
            || (type as INamedTypeSymbol)?.ConstructedFrom.Is(KnownType.Microsoft_EntityFrameworkCore_DbSet_TEntity) == true
            || (type as INamedTypeSymbol)?.OriginalDefinition.ToDisplayString() == "System.Data.Entity.DbSet<TEntity>"
            || type.ToDisplayString() is "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
                or "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions"
                or "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions");

    private static bool IsMessagingTarget(IMethodSymbol method)
    {
        var containing = method.ContainingType?.ToDisplayString() ?? string.Empty;
        return containing.StartsWith("MassTransit", StringComparison.Ordinal)
               || containing.StartsWith("GP.Juno", StringComparison.Ordinal)
               || (method.ContainingType?.AllInterfaces.Any(x => x.ToDisplayString().StartsWith("MassTransit", StringComparison.Ordinal)
                                                                 || x.ToDisplayString().StartsWith("GP.Juno", StringComparison.Ordinal)) ?? false);
    }

    private static bool IsHttpGetAction(IMethodSymbol method) =>
        method.IsControllerActionMethod
        && method.GetAttributes().Select(x => x.AttributeClass?.Name).Any(x => x is "HttpGet" or "HttpGetAttribute");
}
