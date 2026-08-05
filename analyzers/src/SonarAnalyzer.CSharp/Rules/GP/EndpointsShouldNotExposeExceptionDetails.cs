namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EndpointsShouldNotExposeExceptionDetails : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0071";

    private const string MessageFormat = "Do not put '{0}' in a response - return a ProblemDetails without internal details.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> ExceptionDetailMembers = new(StringComparer.Ordinal)
    {
        "Message",
        "StackTrace",
        "Source",
        "InnerException",
        "ToString",
    };

    // Methods that turn a value into the HTTP response body.
    private static readonly HashSet<string> ResponseProducingMethods = new(StringComparer.Ordinal)
    {
        "Ok",
        "BadRequest",
        "Content",
        "Json",
        "Problem",
        "StatusCode",
        "UnprocessableEntity",
        "Conflict",
        "NotFound",
        "ValidationProblem",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);

    private static void AnalyzeMemberAccess(SonarSyntaxNodeReportingContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (!ExceptionDetailMembers.Contains(memberAccess.Name.Identifier.ValueText)
            || context.Model.GetTypeInfo(memberAccess.Expression).Type is not { } receiver
            || !IsException(receiver)
            || !FlowsIntoTheResponse(memberAccess, memberAccess)
            || context.Model.GetEnclosingSymbol(memberAccess.SpanStart) is not IMethodSymbol enclosing
            || !enclosing.IsControllerActionMethod())
        {
            return;
        }

        context.ReportIssue(Rule, memberAccess, $"{receiver.Name}.{memberAccess.Name.Identifier.ValueText}");
    }

    private static bool IsException(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Is(KnownType.System_Exception))
            {
                return true;
            }
        }

        return false;
    }

    // Only reported when the value actually reaches the response: returned from the action, or passed to a method
    // that builds the body. Logging the same value is a different concern and is not reported here.
    private static bool FlowsIntoTheResponse(SyntaxNode node, MemberAccessExpressionSyntax reported)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case ReturnStatementSyntax or ArrowExpressionClauseSyntax:
                    return true;
                // "ex.ToString()" is itself an invocation wrapping the reported member access - it is the value being
                // passed on, not the call that builds the response, so the walk continues past it.
                case InvocationExpressionSyntax invocation when invocation.Expression != reported:
                    return ResponseProducingMethods.Contains(GpCollectionEndpointHelper.GetInvokedMethodName(invocation));
                case MethodDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }
}
