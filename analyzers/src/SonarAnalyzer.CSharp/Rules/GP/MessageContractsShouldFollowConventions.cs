using System.Collections.Concurrent;

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
        context.RegisterCompilationStartAction(start =>
        {
            var behaviorMethods = new ConcurrentDictionary<string, IMethodSymbol>(StringComparer.Ordinal);
            start.RegisterNodeAction(c => AnalyzeInvocation(c, behaviorMethods), SyntaxKind.InvocationExpression);
            start.RegisterCompilationEndAction(c => ReportBehaviorMethods(c, behaviorMethods.Values));
        });

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, IMethodSymbol> behaviorMethods)
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

        var methods = BusinessBehaviorMethods(messageType).ToArray();
        var methodsWithSource = methods.Where(x => MethodIdentifier(x) is not null).ToArray();
        foreach (var method in methodsWithSource)
        {
            behaviorMethods.TryAdd(method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), method);
        }

        // Metadata does not retain source locations. Keep the registration as a fallback for contracts referenced
        // from a compiled assembly, while source contracts are reported directly on every offending method.
        if (methods.Length > 0 && methodsWithSource.Length == 0)
        {
            context.ReportIssue(BehaviorFreeMessageRule, reportNode, messageType.Name);
        }
    }

    private static void ReportBehaviorMethods(SonarCompilationReportingContext context, IEnumerable<IMethodSymbol> methods)
    {
        foreach (var method in methods)
        {
            if (MethodIdentifier(method) is { } identifier)
            {
                context.ReportIssue(CSharpGeneratedCodeRecognizer.Instance, BehaviorFreeMessageRule, identifier.GetLocation(), messageArgs: new[] { method.ContainingType.Name });
            }
        }
    }

    private static SyntaxToken? MethodIdentifier(IMethodSymbol method)
    {
        var declarations = method.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .ToArray();
        return (declarations.FirstOrDefault(x => x.Body is not null || x.ExpressionBody is not null)
                ?? declarations.FirstOrDefault())?.Identifier;
    }

    private static bool TryGetMessageInvocation(SemanticModel model, InvocationExpressionSyntax invocation, out INamedTypeSymbol messageType, out SyntaxNode reportNode)
    {
        messageType = null;
        reportNode = invocation;

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !FluentMessageMethods.Contains(method.Name)
            || !GpMessageContracts.IsMessagingMethod(method))
        {
            return false;
        }

        return TryGetMessageType(model, invocation, method, out messageType, out reportNode);
    }

    private static bool TryGetMessageType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method, out INamedTypeSymbol messageType, out SyntaxNode reportNode)
    {
        if (method.TypeArguments.FirstOrDefault() is INamedTypeSymbol typeArgument)
        {
            messageType = typeArgument;
            reportNode = invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }
                ? genericName.TypeArgumentList.Arguments[0]
                : invocation;
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
    private static IEnumerable<IMethodSymbol> BusinessBehaviorMethods(INamedTypeSymbol messageType) =>
        messageType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(x => x is { MethodKind: MethodKind.Ordinary, IsOverride: false, IsImplicitlyDeclared: false, ExplicitInterfaceImplementations.IsEmpty: true }
                        && !ValueSemanticsMethods.Contains(x.Name)
                        && !IsFactoryMethod(x, messageType));

    // A static method handing back an instance of the contract itself constructs the message rather than acting on it.
    private static bool IsFactoryMethod(IMethodSymbol method, INamedTypeSymbol messageType) =>
        method.IsStatic && method.ReturnType.ToDisplayString() == messageType.ToDisplayString();
}
