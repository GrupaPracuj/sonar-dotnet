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

    private static readonly HashSet<string> NetworkMethodNames = new(StringComparer.Ordinal)
    {
        "Publish",
        "Send",
        "Produce",
        "GetAsync",
        "PostAsync",
        "PutAsync",
        "DeleteAsync",
        "SendAsync",
        "GetFromJsonAsync",
        "PostAsJsonAsync"
    };

    private static readonly HashSet<string> JunoHttpMethodNames = new(StringComparer.Ordinal)
    {
        "Get",
        "Post",
        "Put",
        "Patch",
        "Delete",
        "Head",
        "Options",
        "PostJson",
        "PutJson",
        "PatchJson",
        "DeleteJson",
        "GetJson",
        "GetBytes",
        "PostFormUrlEncoded",
        "PostFormMultipart",
        "PutFormUrlEncoded"
    };

    private static readonly string[] NetworkReceiverHints =
    {
        "http",
        "api",
        "rest",
        "queue",
        "bus",
        "event",
        "stream",
        "broker"
    };

    private const string MessageFormat = "Do not call external network resources inside a database transaction before commit.";
    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeUsingStatement, SyntaxKind.UsingStatement);
        context.RegisterNodeAction(AnalyzeRunInTransactionInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeUsingStatement(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not UsingStatementSyntax usingStatement
            || usingStatement.Declaration?.Variables is not { Count: 1 } variables
            || variables[0].Identifier.ValueText is not { Length: > 0 } transactionVariableName
            || variables[0].Initializer?.Value is not ExpressionSyntax initializerExpression
            || usingStatement.Statement is not BlockSyntax block)
        {
            return;
        }

        var isTransactionStart = IsTransactionStartExpression(initializerExpression);
        var isTransactionScope = IsTransactionScopeExpression(context.Model, initializerExpression);
        if (!isTransactionStart && !isTransactionScope)
        {
            return;
        }

        var statements = block.Statements;
        var boundaryIndex = isTransactionScope
            ? GetCompleteStatementIndex(statements, transactionVariableName)
            : GetCommitStatementIndex(statements, transactionVariableName);

        var lastStatementToAnalyze = boundaryIndex >= 0 ? boundaryIndex - 1 : statements.Count - 1;

        for (var i = 0; i <= lastStatementToAnalyze; i++)
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

    private static bool IsTransactionScopeExpression(SemanticModel model, ExpressionSyntax expression)
    {
        var creation = expression as ObjectCreationExpressionSyntax;
        if (creation is null)
        {
            return false;
        }

        var createdType = model.GetTypeInfo(creation).Type?.ToDisplayString() ?? string.Empty;
        return createdType == "System.Transactions.TransactionScope";
    }

    private static int GetCommitStatementIndex(SyntaxList<StatementSyntax> statements, string transactionVariableName)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            var hasCommit = statements[i].DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(x => IsCommitInvocation(x, transactionVariableName));

            if (hasCommit)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsCommitInvocation(InvocationExpressionSyntax invocation, string transactionVariableName) =>
        transactionVariableName is not null
        && invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: var ownerName },
            Name.Identifier.ValueText: "Commit"
        } && ownerName == transactionVariableName;

    private static int GetCompleteStatementIndex(SyntaxList<StatementSyntax> statements, string transactionVariableName)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            var hasComplete = statements[i].DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(x => IsCompleteInvocation(x, transactionVariableName));

            if (hasComplete)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsCompleteInvocation(InvocationExpressionSyntax invocation, string transactionVariableName) =>
        transactionVariableName is not null
        && invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: var ownerName },
            Name.Identifier.ValueText: "Complete"
        } && ownerName == transactionVariableName;

    private static bool IsExternalNetworkCall(SonarSyntaxNodeReportingContext context, InvocationExpressionSyntax invocation, string transactionVariableName)
    {
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || IsCommitInvocation(invocation, transactionVariableName)
            || IsTransactionOwnerInvocation(invocation, transactionVariableName))
        {
            return false;
        }

        if (IsJunoEventStreamCall(method) || IsJunoHttpCall(method))
        {
            return true;
        }

        if (!NetworkMethodNames.Contains(method.Name))
        {
            return false;
        }

        if (IsLikelyHttpMethod(method.Name))
        {
            return IsLikelyHttpCall(context, invocation, method);
        }

        if (method.Name is "Publish" or "Send" or "Produce")
        {
            return IsLikelyMessagingCall(context, invocation, method);
        }

        return false;
    }

    private static bool IsJunoEventStreamCall(IMethodSymbol method)
    {
        if (method.Name is not ("Publish" or "Send"))
        {
            return false;
        }

        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return namespaceName.IndexOf("GP.Juno.EventStream", StringComparison.OrdinalIgnoreCase) >= 0
               || namespaceName.IndexOf("GP.Juno.Abstractions.EventStream", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsJunoHttpCall(IMethodSymbol method)
    {
        if (!JunoHttpMethodNames.Contains(method.Name) && method.Name != "Send")
        {
            return false;
        }

        var containingTypeName = method.ContainingType?.Name ?? string.Empty;
        var containingTypeDisplayName = method.ContainingType?.ToDisplayString() ?? string.Empty;
        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        return containingTypeName is "HttpSender" or "IHttpClient" or "HttpRequestProperties"
               || containingTypeDisplayName.IndexOf("GP.Juno.HttpApiClient.HttpSending.HttpSender", StringComparison.OrdinalIgnoreCase) >= 0
               || namespaceName.IndexOf("GP.Juno.HttpApiClient.HttpSending", StringComparison.OrdinalIgnoreCase) >= 0
               || namespaceName.IndexOf("GP.Juno.Abstractions.HttpApiClient.HttpSending", StringComparison.OrdinalIgnoreCase) >= 0
               || namespaceName.IndexOf("GP.Juno.HttpClient", StringComparison.OrdinalIgnoreCase) >= 0
               || namespaceName.IndexOf("GP.Juno.Abstractions.HttpClient", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsLikelyHttpMethod(string methodName) =>
        methodName is "GetAsync" or "PostAsync" or "PutAsync" or "DeleteAsync" or "SendAsync" or "GetFromJsonAsync" or "PostAsJsonAsync";

    private static bool IsLikelyHttpCall(SonarSyntaxNodeReportingContext context, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        var typeName = method.ContainingType?.ToDisplayString() ?? string.Empty;
        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if (typeName.IndexOf("HttpClient", StringComparison.OrdinalIgnoreCase) >= 0
            || namespaceName.IndexOf("System.Net.Http", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax { Expression: var receiverExpression })
        {
            return false;
        }

        var receiverTypeName = context.Model.GetTypeInfo(receiverExpression).Type?.ToDisplayString() ?? string.Empty;
        return receiverTypeName.IndexOf("HttpClient", StringComparison.OrdinalIgnoreCase) >= 0
               || receiverTypeName.IndexOf("Http", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsLikelyMessagingCall(SonarSyntaxNodeReportingContext context, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ContainsNetworkHint(namespaceName))
        {
            return true;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax { Expression: var receiverExpression })
        {
            return false;
        }

        var receiverTypeName = context.Model.GetTypeInfo(receiverExpression).Type?.ToDisplayString() ?? string.Empty;
        return ContainsNetworkHint(receiverTypeName);
    }

    private static bool IsTransactionOwnerInvocation(InvocationExpressionSyntax invocation, string transactionVariableName) =>
        transactionVariableName is not null
        && invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: var ownerName }
        } && ownerName == transactionVariableName;

    private static bool ContainsNetworkHint(string value) =>
        NetworkReceiverHints.Any(x => value.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
}
