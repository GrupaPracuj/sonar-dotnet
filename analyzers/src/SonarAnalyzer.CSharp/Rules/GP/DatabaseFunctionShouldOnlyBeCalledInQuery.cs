namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DatabaseFunctionShouldOnlyBeCalledInQuery : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0086";

    private const string MessageFormat = "'{0}' is only meaningful inside a query expression translated to SQL - calling it here throws NotSupportedException at runtime.";

    // The type behind EF.Functions; no KnownType constant exists for this EF-specific type in this codebase, so it
    // is compared by display string directly, the same way GP0017 compares against GP.Juno.Dates.LocalDate.
    private const string DbFunctionsType = "Microsoft.EntityFrameworkCore.DbFunctions";
    private const string DbFunctionAttributeType = "Microsoft.EntityFrameworkCore.DbFunctionAttribute";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.InvocationExpression);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !IsDatabaseOnly(method)
            // IsInExpressionTree walks every enclosing lambda/query clause, at every nesting level, and checks
            // whether it converts to (or takes) System.Linq.Expressions.Expression<TDelegate> - exactly the
            // "translated to SQL" context these members require. UseTrueForAllBase already relies on this same
            // helper to decide whether an EF Core LINQ call sits inside a translatable query, so it is reused here
            // instead of hand-rolling a second ancestor walk.
            || context.IsInExpressionTree())
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.Name);
    }

    private static bool IsDatabaseOnly(IMethodSymbol method) =>
        IsDbFunctionsMember(method) || HasDbFunctionAttribute(method);

    // Like/DateDiffDay/... are extension methods on DbFunctions (the type behind EF.Functions). GetSymbolInfo
    // resolves an extension method called via instance syntax in its reduced form, so the receiver has to be read
    // from ReceiverType rather than ContainingType (which points at the static class declaring the extension, e.g.
    // DbFunctionsExtensions) - the same technique GpHttpCallHelper already uses for this exact reason.
    private static bool IsDbFunctionsMember(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString() == DbFunctionsType
        || (method.IsExtensionMethod && method.ReceiverType?.ToDisplayString() == DbFunctionsType);

    private static bool HasDbFunctionAttribute(IMethodSymbol method) =>
        method.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == DbFunctionAttributeType);
}
