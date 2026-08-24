/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using SonarAnalyzer.CFG.Roslyn;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommitAndPublishShouldNotBeADualWrite : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0048";

    private const string PublishAfterCommitMessage = "This publish follows a database commit with no outbox - if it fails, the data has changed and nobody was told.";
    private const string PublishBeforeCommitMessage = "This publish precedes a database commit with no outbox - if the commit fails, consumers were told about data that does not exist.";

    private static readonly DiagnosticDescriptor PublishAfterCommitRule = DescriptorFactory.Create(RuleId, PublishAfterCommitMessage);
    private static readonly DiagnosticDescriptor PublishBeforeCommitRule = DescriptorFactory.Create(RuleId, PublishBeforeCommitMessage);

    private static readonly HashSet<string> CommitMethods = new(StringComparer.Ordinal)
    {
        "SaveChanges",
        "SaveChangesAsync",
        "Commit",
        "CommitAsync",
    };

    private static readonly HashSet<string> PublishMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(PublishAfterCommitRule, PublishBeforeCommitRule);

    [RuleParameter("outboxTypes", PropertyType.String, "Comma-separated types marking an approved outbox; a method inside such a type is not reported", "")]
    public string OutboxTypes { get; set; } = string.Empty;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);

    private void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        if (methodDeclaration.Body is not { } body || IsInsideApprovedOutbox(context, methodDeclaration))
        {
            return;
        }

        var invocations = body.DescendantNodes(DoesNotBelongToANestedFunction)
            .OfType<InvocationExpressionSyntax>()
            .Select(x => (Invocation: x, Symbol: context.Model.GetSymbolInfo(x).Symbol as IMethodSymbol))
            .Where(x => x.Symbol is not null)
            .ToList();

        if (methodDeclaration.CreateCfg(context.Model, context.Cancel) is not { } cfg)
        {
            return;
        }

        var commitSites = invocations
            .Where(x => IsCommit(x.Symbol))
            .Select(x => InvocationSite(cfg, x.Invocation))
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .ToArray();
        foreach (var publish in invocations.Where(x => IsPublish(x.Symbol)))
        {
            if (InvocationSite(cfg, publish.Invocation) is not { } publishSite)
            {
                continue;
            }

            if (commitSites.Any(x => CanReach(x, publishSite)))
            {
                context.ReportIssue(PublishAfterCommitRule, publish.Invocation);
            }
            else if (commitSites.Any(x => CanReach(publishSite, x)))
            {
                context.ReportIssue(PublishBeforeCommitRule, publish.Invocation);
            }
        }
    }

    private static bool DoesNotBelongToANestedFunction(SyntaxNode node) =>
        node.Kind() != SyntaxKindEx.LocalFunctionStatement && node is not AnonymousFunctionExpressionSyntax;

    private static (BasicBlock Block, int Index)? InvocationSite(ControlFlowGraph cfg, InvocationExpressionSyntax invocation)
    {
        foreach (var block in cfg.Blocks)
        {
            var index = 0;
            foreach (var operation in block.OperationsAndBranchValue.ToExecutionOrder())
            {
                if (operation.Kind == OperationKindEx.Invocation && operation.Syntax.Span == invocation.Span)
                {
                    return (block, index);
                }
                index++;
            }
        }
        return null;
    }

    private static bool CanReach((BasicBlock Block, int Index) from, (BasicBlock Block, int Index) to)
    {
        if (from.Block == to.Block && from.Index < to.Index)
        {
            return true;
        }

        var pending = new Stack<BasicBlock>(from.Block.SuccessorBlocks);
        var visited = new HashSet<BasicBlock>();
        while (pending.Count > 0)
        {
            var block = pending.Pop();
            if (!visited.Add(block))
            {
                continue;
            }
            if (block == to.Block)
            {
                return true;
            }
            foreach (var successor in block.SuccessorBlocks)
            {
                pending.Push(successor);
            }
        }
        return false;
    }

    private static bool IsCommit(IMethodSymbol method) =>
        CommitMethods.Contains(method.Name)
        && (GpJunoTypes.DerivesFrom(method.ContainingType, "Microsoft.EntityFrameworkCore.DbContext")
            || GpJunoTypes.DerivesFrom(method.ContainingType, "System.Data.Entity.DbContext")
            || GpJunoTypes.Implements(method.ContainingType, "GP.Juno.Abstractions.Ado.ITransaction")
            || GpJunoTypes.Implements(method.ContainingType, "System.Data.IDbTransaction"));

    private static bool IsPublish(IMethodSymbol method)
    {
        if (!PublishMethods.Contains(method.Name))
        {
            return false;
        }

        var containing = method.ContainingType?.ToDisplayString() ?? string.Empty;
        return containing.StartsWith("GP.Juno", StringComparison.Ordinal)
               || containing.StartsWith("MassTransit", StringComparison.Ordinal)
               || (method.ContainingType?.AllInterfaces.Any(x => x.ToDisplayString() is { } name
                   && (name.StartsWith("GP.Juno", StringComparison.Ordinal) || name.StartsWith("MassTransit", StringComparison.Ordinal))) ?? false);
    }

    private bool IsInsideApprovedOutbox(SonarSyntaxNodeReportingContext context, SyntaxNode node)
    {
        var outboxTypes = GpEntityTypes.SplitParameter(OutboxTypes);
        if (outboxTypes.Length == 0)
        {
            return false;
        }

        return node.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .Select(x => context.Model.GetDeclaredSymbol(x))
            .Any(x => x is not null && IsApprovedOutbox(x, outboxTypes));
    }

    private static bool IsApprovedOutbox(INamedTypeSymbol type, string[] outboxTypes) =>
        Array.Exists(outboxTypes, x => type.Name == x || type.ToDisplayString() == x)
        || type.AllInterfaces.Any(i => Array.Exists(outboxTypes, x => i.Name == x || i.ToDisplayString() == x))
        || BaseTypes(type).Any(b => Array.Exists(outboxTypes, x => b.Name == x || b.ToDisplayString() == x));

    private static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }
}
