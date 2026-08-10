namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventShouldNotBeRaisedWithNullSenderOrData : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0080";

    // The two checks below report the same rule with two different, fully composed messages - simpler than
    // maintaining a two-argument format string whose second placeholder would only ever hold one of two fixed
    // phrases.
    private const string MessageFormat = "{0}";

    internal const string NullSenderMessageFormat = "Do not pass null as the sender - use 'this' (or the actual raising instance) so subscribers know who raised '{0}'.";
    internal const string NullDataMessageFormat = "Do not pass null as the event data for '{0}' - pass EventArgs.Empty instead, callers expect a non-null value.";
    internal const string NullGenericDataMessageFormat = "Do not pass null as the event data for '{0}' - pass a non-null '{1}' instance instead.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList is not { Arguments: { Count: 2 } arguments }
            || ResolveEventSymbol(invocation, context.Model) is not { } eventSymbol)
        {
            return;
        }

        // Passing null as the sender of a static event has no single agreed-upon replacement (there is no
        // instance to point to), so that case is deliberately left unchecked here.
        if (!eventSymbol.IsStatic && IsNullLiteral(arguments[0].Expression))
        {
            context.ReportIssue(Rule, arguments[0].Expression, string.Format(NullSenderMessageFormat, eventSymbol.Name));
        }

        if (IsNullLiteral(arguments[1].Expression))
        {
            var eventDataType = EventDataType(eventSymbol);
            var message = eventDataType.Is(KnownType.System_EventArgs)
                ? string.Format(NullDataMessageFormat, eventSymbol.Name)
                : string.Format(NullGenericDataMessageFormat, eventSymbol.Name, eventDataType?.Name ?? "event data");
            context.ReportIssue(Rule, arguments[1].Expression, message);
        }
    }

    // Two call shapes raise an event and both end up invoking the same underlying delegate: the direct
    // "MyEvent(sender, args)" syntax - legal only inside the type that declares the event - and the explicit
    // "MyEvent.Invoke(...)" / "MyEvent?.Invoke(...)" form, commonly guarded by "?." to avoid a race with an
    // unsubscribing handler.
    internal static IEventSymbol ResolveEventSymbol(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        // Direct invocation: the identifier (or member access) being called resolves straight to the event.
        if (model.GetSymbolInfo(invocation.Expression).Symbol is IEventSymbol direct)
        {
            return direct;
        }

        // Explicit ".Invoke(...)": the invocation itself resolves to the delegate's Invoke method, and the
        // event is whatever sits immediately before ".Invoke" / "?.Invoke".
        if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke })
        {
            return EventBeforeInvoke(invocation.Expression, model);
        }

        return null;
    }

    internal static ExpressionSyntax EventReceiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke", Expression: MemberAccessExpressionSyntax eventAccess } => eventAccess.Expression,
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke" } => null,
            MemberAccessExpressionSyntax directEventAccess => directEventAccess.Expression,
            MemberBindingExpressionSyntax memberBinding
                when memberBinding.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression is MemberAccessExpressionSyntax eventAccess =>
                eventAccess.Expression,
            _ => null,
        };

    private static IEventSymbol EventBeforeInvoke(ExpressionSyntax invokeAccess, SemanticModel model) =>
        invokeAccess switch
        {
            MemberAccessExpressionSyntax memberAccess => model.GetSymbolInfo(memberAccess.Expression).Symbol as IEventSymbol,
            // Defensive: guards against ever resolving the conditional target to the member binding itself, which
            // would only happen for a malformed or self-referential tree that cannot occur from real source.
            MemberBindingExpressionSyntax memberBinding when memberBinding.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>() is { Expression: { } conditionalTarget } && conditionalTarget != memberBinding =>
                model.GetSymbolInfo(conditionalTarget).Symbol as IEventSymbol,
            _ => null,
        };

    private static bool IsNullLiteral(ExpressionSyntax expression) =>
        expression.IsKind(SyntaxKind.NullLiteralExpression);

    private static ITypeSymbol EventDataType(IEventSymbol eventSymbol) =>
        (eventSymbol.Type as INamedTypeSymbol)?.DelegateInvokeMethod?.Parameters.ElementAtOrDefault(1)?.Type;
}
