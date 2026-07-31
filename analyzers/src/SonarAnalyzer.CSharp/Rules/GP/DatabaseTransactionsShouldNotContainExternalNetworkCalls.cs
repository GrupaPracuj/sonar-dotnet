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

    private static readonly HashSet<string> JunoHttpTargetTypes = new(StringComparer.Ordinal)
    {
        "GP.Juno.HttpApiClient.HttpSending.HttpSender",
        "GP.Juno.HttpClient.IHttpClient",
        "GP.Juno.HttpClient.HttpRequestProperties",
        "GP.Juno.Abstractions.HttpApiClient.HttpSending.HttpSender",
        "GP.Juno.Abstractions.HttpClient.IHttpClient",
        "GP.Juno.Abstractions.HttpClient.HttpRequestProperties"
    };

    private static readonly HashSet<string> FrameworkHttpTargetTypes = new(StringComparer.Ordinal)
    {
        "System.Net.Http.HttpClient",
        "System.Net.Http.HttpMessageInvoker"
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

        return IsJunoServiceBusCall(method) || IsJunoHttpCall(method) || IsFrameworkHttpCall(method);
    }

    private static bool IsJunoServiceBusCall(IMethodSymbol method)
    {
        if (IsJunoServiceBusTargetType(method.ContainingType))
        {
            return true;
        }

        return method.IsExtensionMethod
               && method.Parameters.Length > 0
               && IsJunoServiceBusTargetType(method.Parameters[0].Type);
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

    private static bool IsJunoHttpCall(IMethodSymbol method)
    {
        if (IsJunoHttpTargetType(method.ContainingType))
        {
            return true;
        }

        return method.IsExtensionMethod
               && method.Parameters.Length > 0
               && IsJunoHttpTargetType(method.Parameters[0].Type);
    }

    private static bool IsJunoHttpTargetType(ITypeSymbol type)
    {
        var typeDisplayName = type?.ToDisplayString() ?? string.Empty;
        return JunoHttpTargetTypes.Contains(typeDisplayName);
    }

    private static bool IsFrameworkHttpCall(IMethodSymbol method)
    {
        if (IsFrameworkHttpTargetType(method.ContainingType))
        {
            return true;
        }

        return method.IsExtensionMethod
               && method.Parameters.Length > 0
               && IsFrameworkHttpTargetType(method.Parameters[0].Type);
    }

    private static bool IsFrameworkHttpTargetType(ITypeSymbol type)
    {
        var typeDisplayName = type?.ToDisplayString() ?? string.Empty;
        return FrameworkHttpTargetTypes.Contains(typeDisplayName);
    }


    private static bool IsTransactionOwnerInvocation(InvocationExpressionSyntax invocation, string transactionVariableName) =>
        transactionVariableName is not null
        && invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: var ownerName }
        } && ownerName == transactionVariableName;

}
