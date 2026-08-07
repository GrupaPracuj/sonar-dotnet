namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FlagsEnumShouldNotBeValidatedWithIsDefined : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0092";

    private const string MessageFormat = "Do not use 'Enum.IsDefined' to validate the flags enum '{0}'; combined flag values are valid but may not be named.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "IsDefined" } method
            || !method.ContainingType.Is(KnownType.System_Enum)
            || EnumType(context, invocation, method) is not { TypeKind: TypeKind.Enum } enumType
            || !enumType.GetAttributes().Any(x => x.AttributeClass.Is(KnownType.System_FlagsAttribute)))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, enumType.Name);
    }

    private static ITypeSymbol EnumType(
        SonarSyntaxNodeReportingContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (method.IsGenericMethod && method.TypeArguments.Length == 1)
        {
            return method.TypeArguments[0];
        }

        var lookup = new CSharpMethodParameterLookup(invocation.ArgumentList, method);
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (lookup.TryGetSymbol(argument, out var parameter)
                && parameter.Ordinal == 0
                && argument.Expression is TypeOfExpressionSyntax typeOf)
            {
                return context.Model.GetTypeInfo(typeOf.Type).Type;
            }
        }

        return null;
    }
}
