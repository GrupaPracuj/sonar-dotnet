namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoopVariableShouldNotBeCapturedByDeferredLambda : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0088";

    private const string MessageFormat = "'{0}' is captured by reference and mutated by this loop - every deferred use of this lambda will see the "
                                          + "SAME final value, not the value at each iteration. Copy it to a local variable inside the loop body first.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // The three recognizable "runs later, not now" sinks. Kept as a fixed, narrow list on purpose: anything outside it
    // (an arbitrary method call that might defer the lambda) is deliberately left alone to keep false positives at zero.
    private static readonly HashSet<string> DeferredCollectionMethods = new(StringComparer.Ordinal) { "Add", "Enqueue", "Push" };
    private static readonly HashSet<string> DeferredTaskMethods = new(StringComparer.Ordinal) { "Run", "StartNew", "ContinueWith" };
    private static readonly HashSet<string> DeferredTaskContainingTypes = new(StringComparer.Ordinal)
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.TaskFactory",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.ForStatement);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var forStatement = (ForStatementSyntax)context.Node;
        if (forStatement is not { Declaration: { } declaration, Statement: { } body })
        {
            return;
        }

        var loopVariables = LoopVariables(context.Model, declaration);
        if (loopVariables.Length == 0)
        {
            return;
        }

        foreach (var lambda in body.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
        {
            if (CapturedLoopVariable(context.Model, lambda, loopVariables) is { } capturedVariable && IsDeferredSink(context.Model, lambda))
            {
                context.ReportIssue(Rule, lambda, capturedVariable.Name);
            }
        }
    }

    // The for loop's own initializer variables - e.g. "i" in "for (int i = 0; i < 10; i++)".
    internal static ISymbol[] LoopVariables(SemanticModel model, VariableDeclarationSyntax declaration) =>
        declaration.Variables
            .Select(x => model.GetDeclaredSymbol(x))
            .Where(x => x is not null)
            .ToArray();

    // The first loop variable referenced anywhere inside the lambda - by reference, meaning "captured", since a lambda closes
    // over the variable itself, not a snapshot of its value.
    internal static ISymbol CapturedLoopVariable(SemanticModel model, AnonymousFunctionExpressionSyntax lambda, ISymbol[] loopVariables) =>
        lambda.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(x => model.GetSymbolInfo(x).Symbol)
            .FirstOrDefault(x => x is not null && loopVariables.Any(x.Equals));

    private static bool IsDeferredSink(SemanticModel model, AnonymousFunctionExpressionSyntax lambda)
    {
        if (lambda.Parent is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.AddAssignmentExpression) && assignment.Right == lambda)
        {
            return true; // someEvent += lambda;
        }

        if (lambda.Parent is not ArgumentSyntax argument)
        {
            return false;
        }

        return argument.Parent?.Parent switch
        {
            InvocationExpressionSyntax invocation => IsDeferredInvocation(model, invocation),
            ObjectCreationExpressionSyntax objectCreation => IsThreadConstruction(model, objectCreation),
            _ => false,
        };
    }

    private static bool IsDeferredInvocation(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        if (DeferredCollectionMethods.Contains(method.Name) && invocation.Expression is MemberAccessExpressionSyntax)
        {
            return true; // list.Add(lambda), queue.Enqueue(lambda), stack.Push(lambda)
        }

        var containingType = method.ContainingType?.ToDisplayString();
        return (DeferredTaskMethods.Contains(method.Name) && DeferredTaskContainingTypes.Contains(containingType))
               || (method.Name == "QueueUserWorkItem" && containingType == "System.Threading.ThreadPool");
    }

    private static bool IsThreadConstruction(SemanticModel model, ObjectCreationExpressionSyntax objectCreation) =>
        model.GetTypeInfo(objectCreation).Type?.ToDisplayString() == "System.Threading.Thread";
}
