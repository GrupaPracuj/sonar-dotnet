using System.Collections.Concurrent;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageContractsShouldFollowConventions : SonarDiagnosticAnalyzer
{
    internal const string EventSuffixRuleId = "GP0002";
    internal const string CommandSuffixRuleId = "GP0003";
    internal const string ImmutableMessageRuleId = "GP0004";

    private const string EventSuffixMessage = "Rename event '{0}' to remove the 'Event' suffix.";
    private const string CommandSuffixMessage = "Rename command '{0}' to remove the 'Command' suffix.";
    private const string ImmutableMessageFormat = "Message contract '{0}' should be immutable and must not contain business behavior.";

    private const string JunoNamespacePrefix = "GP.Juno";
    private const string MassTransitNamespacePrefix = "MassTransit";

    private static readonly DiagnosticDescriptor EventSuffixRule = DescriptorFactory.Create(EventSuffixRuleId, EventSuffixMessage);
    private static readonly DiagnosticDescriptor CommandSuffixRule = DescriptorFactory.Create(CommandSuffixRuleId, CommandSuffixMessage);
    private static readonly DiagnosticDescriptor ImmutableMessageRule = DescriptorFactory.Create(ImmutableMessageRuleId, ImmutableMessageFormat);

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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(EventSuffixRule, CommandSuffixRule, ImmutableMessageRule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(startContext =>
        {
            var validatedTypes = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
            startContext.RegisterNodeAction(c => AnalyzeInvocation(c, validatedTypes), SyntaxKind.InvocationExpression);
        });

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, bool> validatedTypes)
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

        if (validatedTypes.TryAdd(messageType.ToDisplayString(), true))
        {
            ValidateMessageShape(context, messageType);
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

    private static void ValidateMessageShape(SonarSyntaxNodeReportingContext context, INamedTypeSymbol messageType)
    {
        foreach (var declaration in messageType.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<TypeDeclarationSyntax>())
        {
            if (ContainsBehavior(declaration))
            {
                context.ReportIssue(ImmutableMessageRule, declaration.Identifier, messageType.Name);
            }
        }
    }

    private static bool ContainsBehavior(TypeDeclarationSyntax declaration) =>
        declaration.Members.OfType<MethodDeclarationSyntax>().Any();
}
