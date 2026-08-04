namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageContractsShouldFollowConventions : SonarDiagnosticAnalyzer
{
    internal const string EventSuffixRuleId = "GP0002";
    internal const string CommandSuffixRuleId = "GP0003";
    internal const string BehaviorFreeMessageRuleId = "GP0004";

    private const string EventSuffixMessage = "Rename event '{0}' to remove the 'Event' suffix.";
    private const string CommandSuffixMessage = "Rename command '{0}' to remove the 'Command' suffix.";
    private const string BehaviorFreeMessageFormat = "Message contract '{0}' should not contain business behavior.";

    private const string JunoNamespacePrefix = "GP.Juno";
    private const string MassTransitNamespacePrefix = "MassTransit";

    private static readonly DiagnosticDescriptor EventSuffixRule = DescriptorFactory.Create(EventSuffixRuleId, EventSuffixMessage);
    private static readonly DiagnosticDescriptor CommandSuffixRule = DescriptorFactory.Create(CommandSuffixRuleId, CommandSuffixMessage);
    private static readonly DiagnosticDescriptor BehaviorFreeMessageRule = DescriptorFactory.Create(BehaviorFreeMessageRuleId, BehaviorFreeMessageFormat);

    private static readonly HashSet<string> FluentMessageMethods = new(StringComparer.Ordinal)
    {
        "Publishes",
        "Sends",
        "Publish",
        "Send"
    };

    private static readonly HashSet<string> MessagePostfixes = new(StringComparer.Ordinal)
    {
        "Event",
        "Command"
    };

    // Members every contract may legitimately declare: these describe the value itself, not business behavior.
    private static readonly HashSet<string> ValueSemanticsMethods = new(StringComparer.Ordinal)
    {
        "Equals",
        "GetHashCode",
        "ToString",
        "Deconstruct",
        "Clone"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(EventSuffixRule, CommandSuffixRule, BehaviorFreeMessageRule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || !TryGetMessageInvocation(context.Model, invocation, out var messageType, out var reportNode))
        {
            return;
        }

        var matchedPostfix = MessagePostfixes.FirstOrDefault(x => messageType.Name.EndsWith(x, StringComparison.Ordinal));
        if (matchedPostfix == "Event")
        {
            context.ReportIssue(EventSuffixRule, reportNode, messageType.Name);
        }
        else if (matchedPostfix == "Command")
        {
            context.ReportIssue(CommandSuffixRule, reportNode, messageType.Name);
        }

        // Reported at the registration site rather than on the contract's own declaration: the declaration usually
        // lives in a different file (often a different project), and a diagnostic reported outside the syntax tree
        // being analyzed is dropped by Roslyn when files are analyzed individually.
        if (HasBusinessBehavior(messageType))
        {
            context.ReportIssue(BehaviorFreeMessageRule, reportNode, messageType.Name);
        }
    }

    private static bool TryGetMessageInvocation(SemanticModel model, InvocationExpressionSyntax invocation, out INamedTypeSymbol messageType, out SyntaxNode reportNode)
    {
        messageType = null;
        reportNode = invocation;

        if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }
            && genericName.TypeArgumentList.Arguments.Count == 1
            && FluentMessageMethods.Contains(genericName.Identifier.ValueText)
            && model.GetTypeInfo(genericName.TypeArgumentList.Arguments[0]).Type is INamedTypeSymbol fluentType)
        {
            messageType = fluentType;
            reportNode = genericName.TypeArgumentList.Arguments[0];
            return true;
        }

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !FluentMessageMethods.Contains(method.Name)
            || !IsJunoOrMassTransitMethod(method))
        {
            return false;
        }

        return TryGetMessageType(model, invocation, method, out messageType, out reportNode);
    }

    private static bool IsJunoOrMassTransitMethod(IMethodSymbol method)
    {
        var methodNamespace = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (IsKnownTransportNamespace(methodNamespace))
        {
            return true;
        }

        var typeNamespace = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return IsKnownTransportNamespace(typeNamespace);
    }

    private static bool IsKnownTransportNamespace(string namespaceName) =>
        namespaceName.StartsWith(JunoNamespacePrefix, StringComparison.Ordinal)
        || namespaceName.StartsWith(MassTransitNamespacePrefix, StringComparison.Ordinal);

    private static bool TryGetMessageType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method, out INamedTypeSymbol messageType, out SyntaxNode reportNode)
    {
        if (method.TypeArguments.FirstOrDefault() is INamedTypeSymbol typeArgument)
        {
            messageType = typeArgument;
            reportNode = invocation;
            return true;
        }

        if (invocation.ArgumentList.Arguments.FirstOrDefault() is { Expression: var firstArgumentExpression }
            && model.GetTypeInfo(firstArgumentExpression).Type is INamedTypeSymbol argumentType
            && argumentType.SpecialType != SpecialType.System_Object)
        {
            messageType = argumentType;
            reportNode = firstArgumentExpression;
            return true;
        }

        messageType = null;
        reportNode = invocation;
        return false;
    }

    // Compiler-generated members (a record's Equals/ToString/Deconstruct), overrides, explicit interface
    // implementations and value-semantics members are not business behavior - only a method the author added to
    // make the message *do* something is.
    private static bool HasBusinessBehavior(INamedTypeSymbol messageType) =>
        messageType.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(x => x is { MethodKind: MethodKind.Ordinary, IsOverride: false, IsImplicitlyDeclared: false, ExplicitInterfaceImplementations.IsEmpty: true }
                      && !ValueSemanticsMethods.Contains(x.Name)
                      && !IsFactoryMethod(x, messageType));

    // A static method handing back an instance of the contract itself constructs the message rather than acting on it.
    private static bool IsFactoryMethod(IMethodSymbol method, INamedTypeSymbol messageType) =>
        method.IsStatic && method.ReturnType.ToDisplayString() == messageType.ToDisplayString();
}
