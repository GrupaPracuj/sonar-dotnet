namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotPublishThroughRawMassTransit : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0034";

    private const string MessageFormat = "Publish through Juno (IPublisher / IMessageSender) instead of MassTransit's '{0}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Only the sending side. IConsumer<T> has no Juno wrapper and is the sanctioned way to handle a message.
    private static readonly HashSet<string> SendingTypes = new(StringComparer.Ordinal)
    {
        "MassTransit.IPublishEndpoint",
        "MassTransit.ISendEndpointProvider",
        "MassTransit.ISendEndpoint",
        "MassTransit.IBus",
    };

    private static readonly HashSet<string> SendingMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
        "GetSendEndpoint",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !SendingMethods.Contains(method.Name)
            || !IsMassTransitSender(method))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.Name);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && type.ToDisplayString() == "RabbitMQ.Client.ConnectionFactory")
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    // The method may be declared on a base interface (IBus derives from IPublishEndpoint and ISendEndpointProvider),
    // so the receiver's own type is checked as well as the declaring type.
    private static bool IsMassTransitSender(IMethodSymbol method) =>
        IsSendingType(method.ContainingType)
        || (method.IsExtensionMethod && IsSendingType(method.ReceiverType));

    private static bool IsSendingType(ITypeSymbol type) =>
        type is not null
        && (SendingTypes.Contains(type.ToDisplayString())
            || type.AllInterfaces.Any(x => SendingTypes.Contains(x.ToDisplayString())));
}
