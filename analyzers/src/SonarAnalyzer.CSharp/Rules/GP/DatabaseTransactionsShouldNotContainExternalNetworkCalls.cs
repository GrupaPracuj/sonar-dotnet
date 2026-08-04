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
        var isTransactionStart = IsTransactionStartExpression(initializerExpression);
        var isTransactionScope = IsTransactionScopeExpression(context.Model, initializerExpression);
        if (!isTransactionStart && !isTransactionScope)
        {
            return;
        }

        var transactionVariableName = variable.Identifier.ValueText;
        var boundaryName = isTransactionScope ? "Complete" : "Commit";
        var boundaryIndex = GetBoundaryStatementIndex(statements, firstIndex, transactionVariableName, boundaryName);
        var lastStatementToAnalyze = boundaryIndex >= 0 ? boundaryIndex - 1 : statements.Count - 1;

        for (var i = firstIndex; i <= lastStatementToAnalyze; i++)
        {
            foreach (var invocation in statements[i].DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (IsExternalNetworkCall(context, invocation, transactionVariableName))
                {
                    context.ReportIssue(Rule, invocation);
                }
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
            ParenthesizedLambdaExpressionSyntax { Block: { } block } => block.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>(),
            SimpleLambdaExpressionSyntax { Block: { } block } => block.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>(),
            ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } expressionBody } => expressionBody.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>(),
            SimpleLambdaExpressionSyntax { ExpressionBody: { } expressionBody } => expressionBody.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>(),
            AnonymousMethodExpressionSyntax { Block: { } block } => block.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>(),
            _ => Enumerable.Empty<InvocationExpressionSyntax>()
        };
    }

    private static bool IsTransactionStartExpression(ExpressionSyntax expression)
    {
        var invocation = expression is AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaitedInvocation }
            ? awaitedInvocation
            : expression as InvocationExpressionSyntax;

        return invocation?.Expression is MemberAccessExpressionSyntax memberAccess
               && TransactionStartMethods.Contains(memberAccess.Name.Identifier.ValueText);
    }

    private static bool IsTransactionScopeExpression(SemanticModel model, ExpressionSyntax expression) =>
        ObjectCreationFactory.TryCreate(expression, out var creation)
        && creation.TypeSymbol(model)?.ToDisplayString() == "System.Transactions.TransactionScope";

    // Index of the statement that ends the transaction (Commit/Complete), or -1 when the block never ends it -
    // in which case every statement in scope is still inside the open transaction.
    private static int GetBoundaryStatementIndex(SyntaxList<StatementSyntax> statements, int firstIndex, string transactionVariableName, string boundaryMethodName)
    {
        for (var i = firstIndex; i < statements.Count; i++)
        {
            var endsTransaction = statements[i].DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(x => IsTransactionMemberInvocation(x, transactionVariableName, boundaryMethodName));

            if (endsTransaction)
            {
                return i;
            }
        }

        return -1;
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
