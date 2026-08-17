namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpCallShouldPropagateCancellationToken : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0027";

    private const string MessageFormat = "Pass the available CancellationToken to this call to another service, so it can be cancelled or time out.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !GpHttpCallHelper.IsHttpCall(method)
            || AvailableCancellationToken(context.Model, invocation) is not { } availableToken
            || CancellationTokenParameter(method) is null
            || PassesAvailableCancellationToken(context.Model, invocation, availableToken))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    // GpHttpCallHelper.IsHttpCall recognizes any call to a known HTTP-ish type - including the GP.Juno fluent HTTP API
    // (IHttpClient, IHttpClientBuilder, HttpRequestProperties) - because that same broad detection is also shared by
    // GP0007 (SharedDictionariesShouldUseJunoDictionaries) and DatabaseTransactionsShouldNotContainExternalNetworkCalls,
    // which only need to know "is this an outgoing HTTP call", not whether it can be cancelled.
    // For GP0027 specifically, a call is only actionable if it can actually be fixed. Verified against the
    // submodules/juno source: none of the GP.Juno fluent API surface (IHttpClient.Send, IHttpClientBuilder.Service,
    // nor any HttpRequestProperties extension such as GetJson/PostJson/PutJson/PatchJson/Delete/...) exposes an
    // overload accepting a CancellationToken anywhere, so those calls can never propagate one and must not be
    // reported. A call is only reported when another member sharing its name in the same containing type - a sibling
    // overload for instance methods, or a sibling extension method for extension methods - actually accepts one.
    internal static IParameterSymbol CancellationTokenParameter(IMethodSymbol method)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        if (definition.Parameters.FirstOrDefault(IsCancellationToken) is { } ownToken)
        {
            return ownToken;
        }

        return definition.ContainingType?.GetMembers(definition.Name)
            .OfType<IMethodSymbol>()
            .Where(x => x.Arity == definition.Arity && x.Parameters.Count(IsCancellationToken) == 1)
            .Select(x => (Method: x, Token: x.Parameters.First(IsCancellationToken)))
            .FirstOrDefault(x => IsSameSignatureWithoutToken(definition, x.Method, x.Token))
            .Token;
    }

    private static bool IsSameSignatureWithoutToken(IMethodSymbol method, IMethodSymbol candidate, IParameterSymbol token)
    {
        if (candidate.Parameters.Length != method.Parameters.Length + 1)
        {
            return false;
        }

        var candidateParameters = candidate.Parameters.Where(x => !x.Equals(token)).ToArray();
        return method.Parameters.Zip(candidateParameters, (left, right) =>
                left.RefKind == right.RefKind && EquivalentType(left.Type, right.Type))
            .All(x => x);
    }

    private static bool EquivalentType(ITypeSymbol left, ITypeSymbol right) =>
        (left, right) switch
        {
            (ITypeParameterSymbol leftParameter, ITypeParameterSymbol rightParameter) =>
                leftParameter.TypeParameterKind == rightParameter.TypeParameterKind && leftParameter.Ordinal == rightParameter.Ordinal,
            (IArrayTypeSymbol leftArray, IArrayTypeSymbol rightArray) =>
                leftArray.Rank == rightArray.Rank && EquivalentType(leftArray.ElementType, rightArray.ElementType),
            (INamedTypeSymbol leftNamed, INamedTypeSymbol rightNamed) =>
                leftNamed.OriginalDefinition.Equals(rightNamed.OriginalDefinition)
                && leftNamed.TypeArguments.Length == rightNamed.TypeArguments.Length
                && leftNamed.TypeArguments.Zip(rightNamed.TypeArguments, EquivalentType).All(x => x),
            _ => left.Equals(right),
        };

    private static bool IsCancellationToken(IParameterSymbol parameter) =>
        parameter.Type.Is(KnownType.System_Threading_CancellationToken);

    private static bool PassesAvailableCancellationToken(SemanticModel model,
                                                         InvocationExpressionSyntax invocation,
                                                         IParameterSymbol availableToken) =>
        ArgumentsWithParameters(invocation, model.GetSymbolInfo(invocation).Symbol as IMethodSymbol)
            .Any(x => IsCancellationToken(x.Parameter)
                      && availableToken.Equals(model.GetSymbolInfo(x.Argument.Expression).Symbol));

    internal static ArgumentSyntax CancellationTokenArgument(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        ArgumentsWithParameters(invocation, method)
            .FirstOrDefault(x => IsCancellationToken(x.Parameter))
            .Argument;

    private static IEnumerable<(ArgumentSyntax Argument, IParameterSymbol Parameter)> ArgumentsWithParameters(InvocationExpressionSyntax invocation,
                                                                                                               IMethodSymbol method)
    {
        if (method is null)
        {
            yield break;
        }

        for (var index = 0; index < invocation.ArgumentList.Arguments.Count; index++)
        {
            var argument = invocation.ArgumentList.Arguments[index];
            IParameterSymbol parameter;
            if (argument.NameColon is { Name.Identifier.ValueText: var parameterName })
            {
                parameter = method.Parameters.FirstOrDefault(x => x.Name == parameterName);
            }
            else
            {
                parameter = index < method.Parameters.Length ? method.Parameters[index] : null;
            }

            if (parameter is not null)
            {
                yield return (argument, parameter);
            }
        }
    }

    // Use the token of the nearest callable scope. A local function parameter is a different symbol from the outer
    // method parameter passed into it, even when both are named "cancellation"; comparing against the outer symbol
    // would therefore report a correctly propagated token inside the local function.
    internal static IParameterSymbol AvailableCancellationToken(SemanticModel model, SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is AnonymousFunctionExpressionSyntax anonymousFunction)
            {
                if (AnonymousFunctionCancellationToken(model, anonymousFunction) is { } lambdaToken)
                {
                    return lambdaToken;
                }

                if (anonymousFunction.ChildTokens().Any(x => x.IsKind(SyntaxKind.StaticKeyword)))
                {
                    return null;
                }
            }

            IMethodSymbol method = ancestor switch
            {
                MethodDeclarationSyntax methodDeclaration => model.GetDeclaredSymbol(methodDeclaration),
                _ when LocalFunctionStatementSyntaxWrapper.IsInstance(ancestor) => model.GetDeclaredSymbol(ancestor) as IMethodSymbol,
                _ => null,
            };
            if (method is not null)
            {
                return method.Parameters.FirstOrDefault(IsCancellationToken);
            }
        }

        return null;
    }

    private static IParameterSymbol AnonymousFunctionCancellationToken(SemanticModel model, AnonymousFunctionExpressionSyntax anonymousFunction) =>
        anonymousFunction switch
        {
            SimpleLambdaExpressionSyntax simple => model.GetDeclaredSymbol(simple.Parameter) as IParameterSymbol,
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters
                .Select(x => model.GetDeclaredSymbol(x))
                .OfType<IParameterSymbol>()
                .FirstOrDefault(IsCancellationToken),
            AnonymousMethodExpressionSyntax { ParameterList: { } parameterList } => parameterList.Parameters
                .Select(x => model.GetDeclaredSymbol(x))
                .OfType<IParameterSymbol>()
                .FirstOrDefault(IsCancellationToken),
            _ => null,
        } is { } parameter && IsCancellationToken(parameter) ? parameter : null;
}
