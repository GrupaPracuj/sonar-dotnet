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
public sealed class DatabaseTransactionsShouldNotContainExternalNetworkCalls : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0008";

    private static readonly HashSet<string> TransactionStartMethods = new(StringComparer.Ordinal)
    {
        "StartTransaction",
        "BeginTransaction"
    };

    private static readonly HashSet<string> TransactionScopeMethods = new(StringComparer.Ordinal)
    {
        "RunInTransaction"
    };

    private static readonly HashSet<string> JunoServiceBusTargetTypes = new(StringComparer.Ordinal)
    {
        "GP.Juno.EventStream.EventStream",
        "GP.Juno.EventStream.JunoServiceBus"
    };

    private const string MessageFormat = "Do not call external network resources inside a database transaction before commit.";
    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeUsingStatement, SyntaxKind.UsingStatement);
        context.RegisterNodeAction(AnalyzeUsingDeclaration, SyntaxKind.LocalDeclarationStatement);
        context.RegisterNodeAction(AnalyzeRunInTransactionInvocation, SyntaxKind.InvocationExpression);
    }

    // using (var transaction = connection.BeginTransaction()) { ... } - the transaction lives for the whole block.
    private static void AnalyzeUsingStatement(SonarSyntaxNodeReportingContext context)
    {
        var usingStatement = (UsingStatementSyntax)context.Node;
        if (SingleDeclaredVariable(usingStatement.Declaration) is not { } variable
            || usingStatement.Statement is not BlockSyntax block)
        {
            return;
        }

        AnalyzeTransactionScope(context, variable, block.Statements, firstIndex: 0);
    }

    // using var transaction = connection.BeginTransaction(); - the transaction lives from here to the end of the
    // enclosing block, so only the statements that follow the declaration are inside it.
    private static void AnalyzeUsingDeclaration(SonarSyntaxNodeReportingContext context)
    {
        var localDeclaration = (LocalDeclarationStatementSyntax)context.Node;
        if (!localDeclaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
            || SingleDeclaredVariable(localDeclaration.Declaration) is not { } variable
            || localDeclaration.Parent is not BlockSyntax block)
        {
            return;
        }

        AnalyzeTransactionScope(context, variable, block.Statements, firstIndex: block.Statements.IndexOf(localDeclaration) + 1);
    }

    private static VariableDeclaratorSyntax SingleDeclaredVariable(VariableDeclarationSyntax declaration) =>
        declaration?.Variables is { Count: 1 } variables
        && variables[0] is { Identifier.ValueText.Length: > 0, Initializer.Value: not null } variable
            ? variable
            : null;

    private static void AnalyzeTransactionScope(SonarSyntaxNodeReportingContext context,
                                               VariableDeclaratorSyntax variable,
                                               SyntaxList<StatementSyntax> statements,
                                               int firstIndex)
    {
        var initializerExpression = variable.Initializer.Value;
        var isTransactionStart = IsTransactionStartExpression(context.Model, initializerExpression);
        var isTransactionScope = IsTransactionScopeExpression(context.Model, initializerExpression);
        if (!isTransactionStart && !isTransactionScope)
        {
            return;
        }

        var transactionVariableName = variable.Identifier.ValueText;
        var invocations = statements
            .Skip(firstIndex)
            .SelectMany(x => x.DescendantNodesAndSelf(DoesNotBelongToANestedFunction).OfType<InvocationExpressionSyntax>())
            .ToArray();
        var networkCalls = invocations.Where(x => IsExternalNetworkCall(context, x, transactionVariableName)).ToArray();
        if (isTransactionScope)
        {
            foreach (var networkCall in networkCalls)
            {
                context.ReportIssue(Rule, networkCall);
            }
            return;
        }

        if (variable.CreateCfg(context.Model, context.Cancel) is not { } cfg)
        {
            return;
        }

        var commitSites = InvocationSites(cfg, invocations.Where(x => IsTransactionMemberInvocation(x, transactionVariableName, "Commit")));
        foreach (var networkCall in networkCalls)
        {
            if (InvocationSite(cfg, networkCall) is { } networkSite && IsReachableWithoutCommit(cfg, networkSite, commitSites))
            {
                context.ReportIssue(Rule, networkCall);
            }
        }
    }

    private static void AnalyzeRunInTransactionInvocation(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || !IsRunInTransactionInvocation(context.Model, invocation))
        {
            return;
        }

        foreach (var nestedInvocation in GetCallbackInvocations(invocation))
        {
            if (IsExternalNetworkCall(context, nestedInvocation, transactionVariableName: null))
            {
                context.ReportIssue(Rule, nestedInvocation);
            }
        }
    }

    private static bool IsRunInTransactionInvocation(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !TransactionScopeMethods.Contains(method.Name))
        {
            return false;
        }

        var typeName = method.ContainingType?.ToDisplayString() ?? string.Empty;
        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return typeName.IndexOf("TransactionalExtensions", StringComparison.OrdinalIgnoreCase) >= 0
               || namespaceName.IndexOf("GP.Juno.Ado", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IEnumerable<InvocationExpressionSyntax> GetCallbackInvocations(InvocationExpressionSyntax invocation)
    {
        var callback = invocation.ArgumentList.Arguments
            .Select(x => x.Expression)
            .FirstOrDefault(x => x is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax);

        return callback switch
        {
            ParenthesizedLambdaExpressionSyntax { Block: { } block } => CallbackInvocations(block),
            SimpleLambdaExpressionSyntax { Block: { } block } => CallbackInvocations(block),
            ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } expressionBody } => CallbackInvocations(expressionBody),
            SimpleLambdaExpressionSyntax { ExpressionBody: { } expressionBody } => CallbackInvocations(expressionBody),
            AnonymousMethodExpressionSyntax { Block: { } block } => CallbackInvocations(block),
            _ => Enumerable.Empty<InvocationExpressionSyntax>()
        };
    }

    private static IEnumerable<InvocationExpressionSyntax> CallbackInvocations(SyntaxNode body) =>
        body.DescendantNodesAndSelf(DoesNotBelongToANestedFunction).OfType<InvocationExpressionSyntax>();

    private static bool IsTransactionStartExpression(SemanticModel model, ExpressionSyntax expression)
    {
        var invocation = expression is AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaitedInvocation }
            ? awaitedInvocation
            : expression as InvocationExpressionSyntax;

        if (invocation is null
            || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !TransactionStartMethods.Contains(method.Name))
        {
            return false;
        }

        var transactionType = UnwrapTask(method.ReturnType);
        if (method.Name == "BeginTransaction")
        {
            return GpJunoTypes.Implements(transactionType, "System.Data.IDbTransaction")
                   || GpJunoTypes.DerivesFrom(transactionType, "System.Data.Common.DbTransaction");
        }

        var ownerName = method.ContainingType?.Name ?? string.Empty;
        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return (ownerName.IndexOf("Transactional", StringComparison.OrdinalIgnoreCase) >= 0
                || namespaceName.StartsWith("GP.Juno.Ado", StringComparison.Ordinal))
               && (GpJunoTypes.Implements(transactionType, "System.IDisposable")
                   || transactionType?.Name.IndexOf("Transaction", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static ITypeSymbol UnwrapTask(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
        && named.OriginalDefinition.IsAny(KnownType.System_Threading_Tasks_Task_T, KnownType.System_Threading_Tasks_ValueTask_TResult)
            ? named.TypeArguments[0]
            : type;

    private static bool IsTransactionScopeExpression(SemanticModel model, ExpressionSyntax expression) =>
        ObjectCreationFactory.TryCreate(expression, out var creation)
        && creation.TypeSymbol(model)?.ToDisplayString() == "System.Transactions.TransactionScope";

    private static bool DoesNotBelongToANestedFunction(SyntaxNode node) =>
        node.Kind() != SyntaxKindEx.LocalFunctionStatement && node is not AnonymousFunctionExpressionSyntax;

    private static Dictionary<BasicBlock, List<int>> InvocationSites(ControlFlowGraph cfg, IEnumerable<InvocationExpressionSyntax> invocations)
    {
        var spans = invocations.Select(x => x.Span).ToHashSet();
        var result = new Dictionary<BasicBlock, List<int>>();
        foreach (var block in cfg.Blocks)
        {
            var index = 0;
            foreach (var operation in block.OperationsAndBranchValue.ToExecutionOrder())
            {
                if (operation.Kind == OperationKindEx.Invocation
                    && operation.Syntax is InvocationExpressionSyntax invocation
                    && spans.Contains(invocation.Span))
                {
                    if (!result.TryGetValue(block, out var indices))
                    {
                        indices = new List<int>();
                        result.Add(block, indices);
                    }
                    indices.Add(index);
                }
                index++;
            }
        }
        return result;
    }

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

    private static bool IsReachableWithoutCommit(ControlFlowGraph cfg,
                                                 (BasicBlock Block, int Index) networkSite,
                                                 Dictionary<BasicBlock, List<int>> commitSites)
    {
        var pending = new Stack<BasicBlock>();
        var visited = new HashSet<BasicBlock>();
        pending.Push(cfg.EntryBlock);
        while (pending.Count > 0)
        {
            var block = pending.Pop();
            if (!visited.Add(block))
            {
                continue;
            }

            if (block == networkSite.Block)
            {
                return !commitSites.TryGetValue(block, out var indices) || indices.All(x => x > networkSite.Index);
            }

            if (commitSites.ContainsKey(block))
            {
                continue;
            }

            foreach (var successor in block.SuccessorBlocks)
            {
                pending.Push(successor);
            }
        }
        return false;
    }

    private static bool IsTransactionMemberInvocation(InvocationExpressionSyntax invocation, string transactionVariableName, string memberName) =>
        transactionVariableName is not null
        && invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: var ownerName },
            Name.Identifier.ValueText: var invokedName
        } && ownerName == transactionVariableName && invokedName == memberName;

    private static bool IsExternalNetworkCall(SonarSyntaxNodeReportingContext context, InvocationExpressionSyntax invocation, string transactionVariableName)
    {
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || IsTransactionOwnerInvocation(invocation, transactionVariableName))
        {
            return false;
        }

        return IsJunoServiceBusCall(context, invocation, method) || GpHttpCallHelper.IsHttpCall(method);
    }

    private static bool IsJunoServiceBusCall(SonarSyntaxNodeReportingContext context, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (IsJunoServiceBusTargetType(method.ContainingType))
        {
            return true;
        }

        if (method.IsExtensionMethod
            // For an extension method called via instance syntax, GetSymbolInfo returns the reduced symbol: Parameters
            // excludes the receiver, so it must be read from ReceiverType (e.g. PublishWithTimeoutExtensions.Publish(this EventStream, ...)).
            && (IsJunoServiceBusTargetType(method.ReceiverType) || (method.Parameters.Length > 0 && IsJunoServiceBusTargetType(method.Parameters[0].Type))))
        {
            return true;
        }

        // GP.Juno.EventStream.EventStream declares no members of its own (it only combines MassTransit's IPublishEndpoint
        // and ISendEndpointProvider), so calling an inherited member like Publish/Send resolves ContainingType to the
        // MassTransit interface that actually declares it, not to EventStream. Fall back to the receiver's static type.
        return invocation.Expression is MemberAccessExpressionSyntax { Expression: var receiverExpression }
               && IsJunoServiceBusTargetType(context.Model.GetTypeInfo(receiverExpression).Type);
    }

    private static bool IsJunoServiceBusTargetType(ITypeSymbol type)
    {
        var typeDisplayName = type?.ToDisplayString() ?? string.Empty;
        if (JunoServiceBusTargetTypes.Contains(typeDisplayName))
        {
            return true;
        }

        var namespaceName = type?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return namespaceName.Equals("GP.Juno.Abstractions.EventStream", StringComparison.Ordinal)
               && type?.Name == "IPublisher";
    }

    // Anything called on the transaction itself (Commit, Rollback, Save, ...) is transaction bookkeeping, not an
    // external call, so it is never reported.
    private static bool IsTransactionOwnerInvocation(InvocationExpressionSyntax invocation, string transactionVariableName) =>
        transactionVariableName is not null
        && invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: var ownerName }
        } && ownerName == transactionVariableName;
}
