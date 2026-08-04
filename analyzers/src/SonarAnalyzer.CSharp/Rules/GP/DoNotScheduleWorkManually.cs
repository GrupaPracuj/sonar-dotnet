namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotScheduleWorkManually : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0038";

    private const string MessageFormat = "Schedule this work through Juno (ISchedulerFactory / IScheduleJobsRegistry) instead of '{0}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> TimerTypes = new(StringComparer.Ordinal)
    {
        "System.Threading.Timer",
        "System.Timers.Timer",
    };

    // Third-party schedulers Juno replaces. These simply never match when the library is not referenced.
    private static readonly HashSet<string> SchedulerTypes = new(StringComparer.Ordinal)
    {
        "Hangfire.RecurringJob",
        "Hangfire.BackgroundJob",
        "Quartz.IScheduler",
        "Quartz.ISchedulerFactory",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && TimerTypes.Contains(type.ToDisplayString()))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType: { } containingType }
            && SchedulerTypes.Contains(containingType.ToDisplayString()))
        {
            context.ReportIssue(Rule, invocation, containingType.Name);
        }
    }
}
