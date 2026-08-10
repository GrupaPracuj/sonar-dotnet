namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GetCollectionEndpointsShouldNotReturnNotFound : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0010";

    private const string MessageFormat = "GET endpoints returning collections should return 200 with an empty collection instead of 404.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeReturnStatement, SyntaxKind.ReturnStatement);
        context.RegisterNodeAction(AnalyzeMinimalApiResult, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeReturnStatement(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not ReturnStatementSyntax { Expression: InvocationExpressionSyntax invocation }
            || context.Model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol method
            || !GpCollectionEndpointHelper.IsHttpGetMethod(method)
            || !GpCollectionEndpointHelper.ReturnsCollection(method, context.Model, context.Node)
            || !IsNotFoundResponse(context.Model, invocation)
            || HasVisibleParentRoute(method)
               && !IsGuardedByReturnedCollectionEmptiness(invocation, context.Model, method.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault()))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static void AnalyzeMinimalApiResult(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsMinimalApiNotFoundResponse(context.Model, invocation)
            || !GpMinimalApi.TryGetInlineHandler(invocation, context.Model, "MapGet", out var handler, out var mapInvocation, out _, out var routeTemplate)
            || !GpMinimalApi.HandlerReturnsCollection(handler, context.Model)
            || EffectiveMinimalRoute(mapInvocation, routeTemplate, context.Model, out var routeIsUnknown) is { } effectiveRoute
               && HasVisibleParentRoute(effectiveRoute)
               && !IsGuardedByReturnedCollectionEmptiness(invocation, context.Model, handler)
            || routeIsUnknown)
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static string EffectiveMinimalRoute(InvocationExpressionSyntax mapInvocation,
                                                string routeTemplate,
                                                SemanticModel model,
                                                out bool routeIsUnknown)
    {
        routeIsUnknown = false;
        var route = routeTemplate;
        var receiver = (mapInvocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        while (receiver is InvocationExpressionSyntax groupInvocation)
        {
            if (model.GetSymbolInfo(groupInvocation).Symbol is IMethodSymbol { Name: "MapGroup" })
            {
                if (groupInvocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } pattern
                    || model.GetConstantValue(pattern) is not { HasValue: true, Value: string prefix })
                {
                    routeIsUnknown = true;
                    return null;
                }

                route = $"{prefix}/{route}";
            }

            receiver = (groupInvocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        }

        return route;
    }

    private static bool IsMinimalApiNotFoundResponse(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (!GpMinimalApi.TryGetResultMethod(model, invocation, out var method))
        {
            return false;
        }

        if (method.Name == "NotFound")
        {
            return true;
        }

        return method.Name == "StatusCode"
               && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } code
               && model.GetConstantValue(code) is { HasValue: true, Value: 404 };
    }

    private static bool HasVisibleParentRoute(IMethodSymbol method)
    {
        var controllerTemplates = RouteTemplates(method.ContainingType.GetAttributes(), "RouteAttribute").ToArray();
        var actionTemplates = RouteTemplates(method.GetAttributes(), "RouteAttribute", "HttpGetAttribute", "HttpGet").ToArray();
        if (actionTemplates.Any(x => x.Length > 0))
        {
            actionTemplates = actionTemplates.Where(x => x.Length > 0).ToArray();
        }

        var effectiveRoutes = actionTemplates.SelectMany(actionTemplate =>
            IsAbsoluteRoute(actionTemplate)
                ? new[] { actionTemplate }
                : controllerTemplates.DefaultIfEmpty(string.Empty).Select(controllerTemplate => $"{controllerTemplate}/{actionTemplate}"))
            .ToArray();
        return effectiveRoutes.Any(HasVisibleParentRoute);
    }

    private static IEnumerable<string> RouteTemplates(IEnumerable<AttributeData> attributes, params string[] attributeNames) =>
        attributes
            .Where(x => attributeNames.Contains(x.AttributeClass?.Name))
            .Select(x => x.ConstructorArguments.FirstOrDefault().Value as string ?? string.Empty);

    private static bool IsAbsoluteRoute(string template) =>
        template.StartsWith("/", StringComparison.Ordinal) || template.StartsWith("~/", StringComparison.Ordinal);

    private static bool HasVisibleParentRoute(string routeTemplate)
    {
        var segments = routeTemplate.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].IndexOf('{') >= 0
                && segments.Skip(i + 1).Any(x => x.IndexOf('{') < 0))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsGuardedByReturnedCollectionEmptiness(InvocationExpressionSyntax invocation, SemanticModel model, SyntaxNode boundary)
    {
        var returnedCollections = CollectionSymbolsReturnedByOk(boundary, model).ToHashSet();
        if (returnedCollections.Count == 0)
        {
            return false;
        }

        var ancestors = invocation.Ancestors().TakeWhile(x => x != boundary).ToArray();
        return ancestors.OfType<IfStatementSyntax>()
            .Any(x => x.Statement.Span.Contains(invocation.Span) && IsCollectionEmptyCondition(x.Condition, model, returnedCollections)
                      || x.Else?.Statement.Span.Contains(invocation.Span) == true && IsCollectionNonEmptyCondition(x.Condition, model, returnedCollections))
        || ancestors.OfType<ConditionalExpressionSyntax>()
            .Any(x => x.WhenTrue.Span.Contains(invocation.Span) && IsCollectionEmptyCondition(x.Condition, model, returnedCollections)
                      || x.WhenFalse.Span.Contains(invocation.Span) && IsCollectionNonEmptyCondition(x.Condition, model, returnedCollections));
    }

    private static IEnumerable<ISymbol> CollectionSymbolsReturnedByOk(SyntaxNode boundary, SemanticModel model) =>
        boundary?.DescendantNodesAndSelf(x => x.Kind() != SyntaxKindEx.LocalFunctionStatement && x is not AnonymousFunctionExpressionSyntax || x == boundary)
            .OfType<InvocationExpressionSyntax>()
            .Where(x => GpMinimalApi.TryGetResultMethod(model, x, out var resultMethod) && resultMethod.Name == "Ok"
                        || GpCollectionEndpointHelper.GetInvokedMethodName(x) == "Ok")
            .Select(x => x.ArgumentList.Arguments.FirstOrDefault()?.Expression)
            .WhereNotNull()
            .Select(x => model.GetSymbolInfo(x))
            .Select(x => x.Symbol)
            .WhereNotNull()
        ?? Enumerable.Empty<ISymbol>();

    private static bool IsCollectionEmptyCondition(ExpressionSyntax condition, SemanticModel model, HashSet<ISymbol> returnedCollections)
    {
        condition = RemoveParentheses(condition);
        if (condition is BinaryExpressionSyntax binary)
        {
            if (binary.IsKind(SyntaxKind.LogicalAndExpression))
            {
                return IsCollectionEmptyCondition(binary.Left, model, returnedCollections) || IsCollectionEmptyCondition(binary.Right, model, returnedCollections);
            }

            return binary.IsKind(SyntaxKind.EqualsExpression)
                   && (IsZero(binary.Left, model) && IsReturnedCollectionCount(binary.Right, model, returnedCollections)
                       || IsZero(binary.Right, model) && IsReturnedCollectionCount(binary.Left, model, returnedCollections));
        }

        return condition is PrefixUnaryExpressionSyntax unary
               && unary.IsKind(SyntaxKind.LogicalNotExpression)
               && IsReturnedCollectionAny(RemoveParentheses(unary.Operand), model, returnedCollections);
    }

    private static bool IsCollectionNonEmptyCondition(ExpressionSyntax condition, SemanticModel model, HashSet<ISymbol> returnedCollections)
    {
        condition = RemoveParentheses(condition);
        if (IsReturnedCollectionAny(condition, model, returnedCollections))
        {
            return true;
        }

        if (condition is not BinaryExpressionSyntax binary)
        {
            return false;
        }

        if (binary.IsKind(SyntaxKind.LogicalAndExpression))
        {
            return IsCollectionNonEmptyCondition(binary.Left, model, returnedCollections) || IsCollectionNonEmptyCondition(binary.Right, model, returnedCollections);
        }

        return binary.Kind() is SyntaxKind.NotEqualsExpression or SyntaxKind.GreaterThanExpression
               && (IsZero(binary.Left, model) && IsReturnedCollectionCount(binary.Right, model, returnedCollections)
                   || IsZero(binary.Right, model) && IsReturnedCollectionCount(binary.Left, model, returnedCollections));
    }

    private static bool IsZero(ExpressionSyntax expression, SemanticModel model) =>
        model.GetConstantValue(expression) is { HasValue: true, Value: int value } && value == 0;

    private static bool IsReturnedCollectionCount(ExpressionSyntax expression, SemanticModel model, HashSet<ISymbol> returnedCollections)
    {
        expression = RemoveParentheses(expression);
        if (expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" or "Length", Expression: { } receiver }
            && model.GetTypeInfo(receiver).Type is { } receiverType
            && IsReturnedCollection(receiver, model, returnedCollections))
        {
            return GpCollectionEndpointHelper.IsCollectionLike(receiverType);
        }

        return expression is InvocationExpressionSyntax invocation
               && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "Count" } method
               && (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() == "System.Linq.Enumerable"
               && CollectionReceiverType(invocation, method, model) is { } collectionType
               && GpCollectionEndpointHelper.IsCollectionLike(collectionType)
               && CollectionReceiverExpression(invocation, method) is { } countReceiver
               && IsReturnedCollection(countReceiver, model, returnedCollections);
    }

    private static bool IsReturnedCollectionAny(ExpressionSyntax expression, SemanticModel model, HashSet<ISymbol> returnedCollections) =>
        expression is InvocationExpressionSyntax invocation
        && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "Any" } method
        && (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() == "System.Linq.Enumerable"
        && CollectionReceiverType(invocation, method, model) is { } collectionType
        && GpCollectionEndpointHelper.IsCollectionLike(collectionType)
        && CollectionReceiverExpression(invocation, method) is { } receiver
        && IsReturnedCollection(receiver, model, returnedCollections);

    private static bool IsReturnedCollection(ExpressionSyntax expression, SemanticModel model, HashSet<ISymbol> returnedCollections) =>
        model.GetSymbolInfo(expression).Symbol is { } symbol && returnedCollections.Contains(symbol);

    private static ExpressionSyntax CollectionReceiverExpression(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method.ReducedFrom is not null && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } receiver })
        {
            return receiver;
        }

        return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    }

    private static ITypeSymbol CollectionReceiverType(InvocationExpressionSyntax invocation, IMethodSymbol method, SemanticModel model)
    {
        if (method.ReducedFrom is not null
            && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } receiver })
        {
            return model.GetTypeInfo(receiver).Type;
        }

        var firstParameter = method.Parameters.FirstOrDefault();
        var lookup = new CSharpMethodParameterLookup(invocation, method);
        return firstParameter is not null
               && lookup.TryGetSyntax(firstParameter, out var arguments)
               && arguments.Length == 1
            ? model.GetTypeInfo((ExpressionSyntax)arguments[0]).Type
            : null;
    }

    private static ExpressionSyntax RemoveParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }
        return expression;
    }

    private static bool IsNotFoundResponse(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        var methodName = GpCollectionEndpointHelper.GetInvokedMethodName(invocation);

        if (methodName == "NotFound")
        {
            return true;
        }

        if (methodName != "StatusCode"
            || invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not ExpressionSyntax codeExpression
            || model.GetConstantValue(codeExpression) is not { HasValue: true, Value: int statusCode })
        {
            return false;
        }

        return statusCode == 404;
    }
}
