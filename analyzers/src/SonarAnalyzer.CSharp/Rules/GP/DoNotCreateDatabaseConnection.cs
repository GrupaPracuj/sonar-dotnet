/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotCreateDatabaseConnection : SonarDiagnosticAnalyzer
{
    internal const string ConnectionRuleId = "GP0035";
    internal const string TransactionRuleId = "GP0129";
    internal const string CancellationRuleId = "GP0130";

    private const string ConnectionMessage = "Obtain the connection from Juno: express the work as an IDbExecute, or use Dapper on a connection created by IAdoConnectionFactory.";
    private const string TransactionMessage = "Pass the active transaction to this Dapper operation.";
    private const string CancellationMessage = "Pass the CancellationToken through Dapper CommandDefinition.";

    private static readonly DiagnosticDescriptor ConnectionRule = DescriptorFactory.Create(ConnectionRuleId, ConnectionMessage);
    private static readonly DiagnosticDescriptor TransactionRule = DescriptorFactory.Create(TransactionRuleId, TransactionMessage);
    private static readonly DiagnosticDescriptor CancellationRule = DescriptorFactory.Create(CancellationRuleId, CancellationMessage);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(ConnectionRule, TransactionRule, CancellationRule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && GpJunoTypes.DerivesFrom(type, "System.Data.Common.DbConnection"))
        {
            context.ReportIssue(ConnectionRule, creation.Expression);
        }
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (IsInsideJuno(context)
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (IsDapperDatabaseOperation(method))
        {
            AnalyzeDapper(context, invocation, method);
        }
        else if (method.Name == "CreateConnection"
                 && GpJunoTypes.DerivesFrom(method.ContainingType, "System.Data.Common.DbProviderFactory"))
        {
            context.ReportIssue(ConnectionRule, invocation);
        }
    }

    private static void AnalyzeDapper(SonarSyntaxNodeReportingContext context, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (context.Model.GetEnclosingSymbol(invocation.SpanStart)?.ContainingType is { } containingType
            && ControllersShouldNotUseInfrastructureDirectly.IsDbExecute(containingType))
        {
            if (AvailableTransactionParameter(context.Model, invocation) is { } transaction
                && PassesTransaction(context.Model, invocation, method, transaction) == false)
            {
                context.ReportIssue(TransactionRule, invocation);
            }
            return;
        }

        if (DapperConnection(invocation, method) is not { } connection)
        {
            context.ReportIssue(ConnectionRule, invocation);
            return;
        }

        var origin = ConnectionOriginOf(context.Model, connection, new HashSet<ISymbol>());
        if (origin == ConnectionOrigin.Manual)
        {
            // The connection creation itself is reported, so do not duplicate the diagnostic at every operation.
            return;
        }

        var helperTransaction = HelperTransaction(context.Model, invocation, connection);
        if (origin is not (ConnectionOrigin.AdoFactory or ConnectionOrigin.HelperParameter))
        {
            context.ReportIssue(ConnectionRule, invocation);
            return;
        }
        if (origin == ConnectionOrigin.HelperParameter && !IsAdoHelper(context.Model, invocation, connection))
        {
            context.ReportIssue(ConnectionRule, invocation);
            return;
        }

        var activeTransaction = ActiveTransaction(context.Model, invocation);
        if (activeTransaction is { } active
            && (!SameSymbol(context.Model, connection, active.Connection)
                || PassesTransaction(context.Model, invocation, method, active.Transaction) == false))
        {
            context.ReportIssue(TransactionRule, invocation);
            return;
        }

        if (helperTransaction is { } helper
            && PassesTransaction(context.Model, invocation, method, helper) == false)
        {
            context.ReportIssue(TransactionRule, invocation);
            return;
        }

        if (!PassesCancellationThroughCommand(context.Model, invocation, method))
        {
            context.ReportIssue(CancellationRule, invocation);
        }
    }

    private static bool IsDapperDatabaseOperation(IMethodSymbol method) =>
        (method.ContainingType.Is(KnownType.Dapper_SqlMapper)
         || method.ReducedFrom?.ContainingType.Is(KnownType.Dapper_SqlMapper) == true)
        && (method.Name.StartsWith("Query", StringComparison.Ordinal)
            || method.Name.StartsWith("Execute", StringComparison.Ordinal));

    private static IParameterSymbol AvailableTransactionParameter(SemanticModel model, SyntaxNode node)
    {
        for (var symbol = model.GetEnclosingSymbol(node.SpanStart); symbol is IMethodSymbol method; symbol = method.ContainingSymbol)
        {
            if (method.Parameters.FirstOrDefault(x => IsDbTransaction(x.Type)) is { } transaction)
            {
                return transaction;
            }
        }

        return null;
    }

    private static IParameterSymbol HelperTransaction(SemanticModel model, SyntaxNode node, ExpressionSyntax connection)
    {
        if (model.GetSymbolInfo(connection.RemoveParentheses()).Symbol is not IParameterSymbol connectionParameter
            || model.GetEnclosingSymbol(node.SpanStart) is not IMethodSymbol method
            || !method.Parameters.Contains(connectionParameter))
        {
            return null;
        }

        return method.Parameters.FirstOrDefault(x => IsDbTransaction(x.Type));
    }

    private static bool IsAdoHelper(SemanticModel model, SyntaxNode node, ExpressionSyntax connection) =>
        model.GetSymbolInfo(connection.RemoveParentheses()).Symbol is IParameterSymbol connectionParameter
        && model.GetEnclosingSymbol(node.SpanStart) is IMethodSymbol method
        && method.Parameters.Contains(connectionParameter)
        && method.Parameters.Any(x => IsCancellationToken(x.Type));

    private static bool? PassesTransaction(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ISymbol availableTransaction)
    {
        var mappings = new CSharpMethodParameterLookup(invocation, method).GetAllArgumentParameterMappings().ToArray();
        if (method.Parameters.Any(x => x.Name == "transaction" && IsDbTransaction(x.Type)))
        {
            return mappings
                .Where(x => x.Symbol.Name == "transaction" && IsDbTransaction(x.Symbol.Type))
                .Select(x => x.Node?.Expression)
                .Any(x => IsExactSymbol(model, x, availableTransaction));
        }

        var command = mappings
            .FirstOrDefault(x => x.Symbol.Type.Is(KnownType.Dapper_CommandDefinition))
            .Node?.Expression;
        return CommandCreation(model, command) is { ArgumentList: { } arguments } creation
               && creation.MethodSymbol(model) is { } constructor
            ? new CSharpMethodParameterLookup(arguments, constructor).GetAllArgumentParameterMappings()
                .Where(x => x.Symbol.Name == "transaction" && IsDbTransaction(x.Symbol.Type))
                .Select(x => x.Node?.Expression)
                .Any(x => IsExactSymbol(model, x, availableTransaction))
            : null;
    }

    private static bool PassesCancellationThroughCommand(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        var command = new CSharpMethodParameterLookup(invocation, method).GetAllArgumentParameterMappings()
            .FirstOrDefault(x => x.Symbol.Type.Is(KnownType.Dapper_CommandDefinition))
            .Node?.Expression;
        if (command is null)
        {
            return false;
        }

        var creation = CommandCreation(model, command);
        if (creation is null)
        {
            // A command received by a helper may already contain the token; do not guess.
            return true;
        }

        return creation.ArgumentList is { } arguments
               && creation.MethodSymbol(model) is { } constructor
               && new CSharpMethodParameterLookup(arguments, constructor).GetAllArgumentParameterMappings()
                   .Any(x => IsCancellationToken(x.Symbol.Type)
                             && x.Node?.Expression is { } expression
                             && !IsNoneCancellationToken(model, expression));
    }

    private static IObjectCreation CommandCreation(SemanticModel model, ExpressionSyntax expression)
    {
        if (expression is null)
        {
            return null;
        }

        if (ObjectCreationFactory.TryCreate(expression) is { } creation)
        {
            return creation;
        }

        return model.GetSymbolInfo(expression).Symbol is ILocalSymbol local
            ? local.DeclaringSyntaxReferences
                .Select(x => x.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .Select(x => x.Initializer?.Value)
                .Select(ObjectCreationFactory.TryCreate)
                .WhereNotNull()
                .SingleOrDefault()
            : null;
    }

    private static ExpressionSyntax DapperConnection(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method.ReducedFrom is not null
            && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } receiver })
        {
            return receiver;
        }

        return new CSharpMethodParameterLookup(invocation, method).GetAllArgumentParameterMappings()
            .Where(x => IsDbConnection(x.Symbol.Type))
            .Select(x => x.Node?.Expression)
            .FirstOrDefault(x => x is not null);
    }

    private static ConnectionOrigin ConnectionOriginOf(SemanticModel model, ExpressionSyntax expression, HashSet<ISymbol> visited)
    {
        expression = Unwrap(expression);
        if (expression is InvocationExpressionSyntax invocation
            && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol invoked)
        {
            if (IsAdoConnectionFactoryCreate(invoked))
            {
                return ConnectionOrigin.AdoFactory;
            }

            if (invoked.Name == "CreateConnection"
                && GpJunoTypes.DerivesFrom(invoked.ContainingType, "System.Data.Common.DbProviderFactory"))
            {
                return ConnectionOrigin.Manual;
            }
        }

        if (ObjectCreationFactory.TryCreate(expression) is { } creation
            && creation.TypeSymbol(model) is { } createdType
            && GpJunoTypes.DerivesFrom(createdType, "System.Data.Common.DbConnection"))
        {
            return ConnectionOrigin.Manual;
        }

        return model.GetSymbolInfo(expression).Symbol switch
        {
            IParameterSymbol => ConnectionOrigin.HelperParameter,
            ILocalSymbol local when visited.Add(local) && LocalInitializer(local) is { } initializer =>
                ConnectionOriginOf(model, initializer, visited),
            _ => ConnectionOrigin.Unknown,
        };
    }

    private static (ISymbol Transaction, ExpressionSyntax Connection)? ActiveTransaction(
        SemanticModel model,
        InvocationExpressionSyntax operation)
    {
        if (model.GetEnclosingSymbol(operation.SpanStart) is not IMethodSymbol method
            || operation.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() is not { } declaration)
        {
            return null;
        }

        return declaration.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(x => x.SpanStart < operation.SpanStart
                        && model.GetEnclosingSymbol(x.SpanStart)?.Equals(method) == true)
            .Select(x => (Declaration: x, Symbol: model.GetDeclaredSymbol(x) as ILocalSymbol))
            .Where(x => x.Symbol is not null
                        && model.LookupSymbols(operation.SpanStart, name: x.Symbol.Name).Any(x.Symbol.Equals))
            .Select(x => (x.Declaration, x.Symbol, Connection: TransactionConnection(model, x.Declaration)))
            .Where(x => x.Connection is not null && !TransactionEnded(model, declaration, x.Symbol, x.Declaration.SpanStart, operation.SpanStart))
            .Select(x => ((ISymbol Transaction, ExpressionSyntax Connection)?)((ISymbol)x.Symbol, x.Connection))
            .LastOrDefault();
    }

    private static ExpressionSyntax TransactionConnection(SemanticModel model, VariableDeclaratorSyntax declaration)
    {
        if (declaration.Initializer?.Value is not { } value
            || Unwrap(value) is not InvocationExpressionSyntax invocation
            || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "BeginTransaction" or "BeginTransactionAsync" })
        {
            return null;
        }

        return invocation.Expression is MemberAccessExpressionSyntax { Expression: { } connection }
            ? connection
            : null;
    }

    private static bool TransactionEnded(
        SemanticModel model,
        BaseMethodDeclarationSyntax method,
        ISymbol transaction,
        int transactionStart,
        int operationStart) =>
        method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(x => x.SpanStart > transactionStart && x.SpanStart < operationStart)
            .Any(x => x.Expression is MemberAccessExpressionSyntax
                      {
                          Expression: { } receiver,
                          Name.Identifier.ValueText: "Commit" or "CommitAsync" or "Rollback" or "RollbackAsync",
                      }
                      && IsExactSymbol(model, receiver, transaction));

    private static bool SameSymbol(SemanticModel model, ExpressionSyntax first, ExpressionSyntax second) =>
        model.GetSymbolInfo(Unwrap(first)).Symbol is { } firstSymbol
        && firstSymbol.Equals(model.GetSymbolInfo(Unwrap(second)).Symbol);

    private static bool IsAdoConnectionFactoryCreate(IMethodSymbol method) =>
        method.Name == "CreateConnection"
        && (method.ContainingType.ToDisplayString() == "GP.Juno.Ado.IAdoConnectionFactory"
            || GpJunoTypes.Implements(method.ContainingType, "GP.Juno.Ado.IAdoConnectionFactory"));

    private static ExpressionSyntax LocalInitializer(ILocalSymbol local) =>
        local.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<VariableDeclaratorSyntax>()
            .Select(x => x.Initializer?.Value)
            .SingleOrDefault(x => x is not null);

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        expression = (ExpressionSyntax)expression.RemoveParentheses();
        return expression is AwaitExpressionSyntax awaitExpression
            ? (ExpressionSyntax)awaitExpression.Expression.RemoveParentheses()
            : expression;
    }

    private static bool IsNoneCancellationToken(SemanticModel model, ExpressionSyntax expression) =>
        expression.RemoveParentheses() is DefaultExpressionSyntax
        || expression.IsKind(SyntaxKindEx.DefaultLiteralExpression)
        || model.GetSymbolInfo(expression).Symbol is IPropertySymbol
        {
            IsStatic: true,
            Name: "None",
            ContainingType: { } containingType,
        }
        && containingType.Is(KnownType.System_Threading_CancellationToken);

    private static bool IsExactSymbol(SemanticModel model, ExpressionSyntax expression, ISymbol expected) =>
        expression is not null && expected.Equals(model.GetSymbolInfo(expression).Symbol);

    private static bool IsDbConnection(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "IDbConnection" } connection
        && connection.ContainingNamespace.ToDisplayString() == "System.Data"
        || GpJunoTypes.DerivesFrom(type, "System.Data.Common.DbConnection");

    private static bool IsDbTransaction(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "IDbTransaction" } transaction
        && transaction.ContainingNamespace.ToDisplayString() == "System.Data"
        || GpJunoTypes.DerivesFrom(type, "System.Data.Common.DbTransaction");

    private static bool IsCancellationToken(ITypeSymbol type) =>
        type.Is(KnownType.System_Threading_CancellationToken);

    private static bool IsInsideJuno(SonarSyntaxNodeReportingContext context)
    {
        var containingNamespace = context.Model.GetEnclosingSymbol(context.Node.SpanStart)?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return containingNamespace == "GP.Juno" || containingNamespace.StartsWith("GP.Juno.", StringComparison.Ordinal);
    }

    private enum ConnectionOrigin
    {
        Unknown,
        AdoFactory,
        HelperParameter,
        Manual,
    }
}
