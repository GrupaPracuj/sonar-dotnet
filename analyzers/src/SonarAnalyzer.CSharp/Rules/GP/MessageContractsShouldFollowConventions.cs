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

    private static readonly DiagnosticDescriptor EventSuffixRule = DescriptorFactory.Create(EventSuffixRuleId, EventSuffixMessage);
    private static readonly DiagnosticDescriptor CommandSuffixRule = DescriptorFactory.Create(CommandSuffixRuleId, CommandSuffixMessage);
    private static readonly DiagnosticDescriptor ImmutableMessageRule = DescriptorFactory.Create(ImmutableMessageRuleId, ImmutableMessageFormat);

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
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || memberAccess.Name is not GenericNameSyntax genericName
            || genericName.TypeArgumentList.Arguments.Count != 1)
        {
            return;
        }

        var methodName = genericName.Identifier.ValueText;
        if (methodName is not ("Publishes" or "Sends"))
        {
            return;
        }

        var typeSyntax = genericName.TypeArgumentList.Arguments[0];
        if (context.Model.GetTypeInfo(typeSyntax).Type is not INamedTypeSymbol messageType)
        {
            return;
        }

        if (methodName == "Publishes" && messageType.Name.EndsWith("Event", StringComparison.Ordinal))
        {
            context.ReportIssue(EventSuffixRule, typeSyntax, messageType.Name);
        }

        if (methodName == "Sends" && messageType.Name.EndsWith("Command", StringComparison.Ordinal))
        {
            context.ReportIssue(CommandSuffixRule, typeSyntax, messageType.Name);
        }

        if (validatedTypes.TryAdd(messageType.ToDisplayString(), true))
        {
            ValidateMessageShape(context, messageType);
        }
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
