namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageContractMustBePublic : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0060";

    private const string MessageFormat = "'{0}' is not public, so no other service can reference this contract.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> MessagingMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
        "RespondAsync",
        "Publishes",
        "Sends",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeConsumerDeclaration, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !MessagingMethods.Contains(method.Name)
            || !GpMessageContracts.IsMessagingMethod(method)
            || MessageType(context.Model, invocation, method) is not { } messageType
            || !IsDeclaredInThisAssembly(messageType, context.Compilation)
            || messageType.EffectiveAccessibility == Accessibility.Public)
        {
            return;
        }

        context.ReportIssue(Rule, invocation, messageType.Name);
    }

    private static void AnalyzeConsumerDeclaration(SonarSyntaxNodeReportingContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(classDeclaration) is not { } type)
        {
            return;
        }

        foreach (var consumed in type.AllInterfaces
            .Where(GpMessageContracts.IsConsumerInterface)
            .Select(x => x.TypeArguments[0])
            .Where(x => IsDeclaredInThisAssembly(x, context.Compilation) && x.EffectiveAccessibility != Accessibility.Public))
        {
            context.ReportIssue(Rule, classDeclaration.Identifier, consumed.Name);
        }
    }

    // A contract from a referenced assembly is already public enough to have been referenced, and its accessibility is
    // not this project's to change.
    private static bool IsDeclaredInThisAssembly(ITypeSymbol type, Compilation compilation) =>
        type.ContainingAssembly is { } assembly && assembly.Name == compilation.AssemblyName;

    private static ITypeSymbol MessageType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.TypeArguments.FirstOrDefault()
        ?? (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } firstArgument
            ? model.GetTypeInfo(firstArgument).Type
            : null);
}
