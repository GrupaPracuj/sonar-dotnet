namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WholeContractShouldNotBeLogged : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0074";

    private const string MessageFormat = "Do not log the whole contract '{0}' - log the fields the diagnosis needs.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList is not { } argumentList || !GpLoggingHelper.IsLoggingCall(context.Model, invocation))
        {
            return;
        }

        foreach (var argument in argumentList.Arguments)
        {
            if (ContractArgumentType(context.Model, argument.Expression) is { } contractType)
            {
                context.ReportIssue(Rule, argument, contractType.Name);
                return; // one finding per logging call is enough
            }
        }
    }

    // The whole object, not one of its fields: "message" matches, "message.OrderId" does not, because the type of a
    // member access is the member's type rather than the contract's.
    private static INamedTypeSymbol ContractArgumentType(SemanticModel model, ExpressionSyntax expression) =>
        model.GetTypeInfo(expression).Type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named
        && GpMessageContracts.HasContractName(named.Name)
            ? named
            : null;
}
