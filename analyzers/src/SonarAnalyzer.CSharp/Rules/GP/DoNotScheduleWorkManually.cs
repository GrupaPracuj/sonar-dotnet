/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotScheduleWorkManually : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0038";

    private const string MessageFormat = "Schedule this work through Juno (ISchedulerFactory / IScheduleJobsRegistry) instead of '{0}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private const string ThreadingTimer = "System.Threading.Timer";
    private const string TimersTimer = "System.Timers.Timer";

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
            && IsRecurringTimer(creation, type, context.Model))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    private static bool IsRecurringTimer(IObjectCreation creation, ITypeSymbol type, SemanticModel model) =>
        type.ToDisplayString() switch
        {
            ThreadingTimer => IsRecurringThreadingTimer(creation, model),
            TimersTimer => IsRecurringTimersTimer(creation, model),
            _ => false,
        };

    private static bool IsRecurringThreadingTimer(IObjectCreation creation, SemanticModel model)
    {
        if (creation.MethodSymbol(model) is not { } constructor
            || creation.ArgumentList is not { } argumentList)
        {
            return false;
        }

        var period = new CSharpMethodParameterLookup(argumentList, constructor).GetAllArgumentParameterMappings()
            .FirstOrDefault(x => x.Symbol.Name == "period");
        return period.Node is not null && IsKnownPositivePeriod(period.Node.Expression, model);
    }

    private static bool IsRecurringTimersTimer(IObjectCreation creation, SemanticModel model)
    {
        if (creation.InitializerExpressions?
                .OfType<AssignmentExpressionSyntax>()
                .Any(x => model.GetSymbolInfo(x.Left).Symbol is IPropertySymbol
                          {
                              Name: "AutoReset",
                              ContainingType: { } containingType,
                          }
                          && containingType.ToDisplayString() == TimersTimer
                          && model.GetConstantValue(x.Right) is { HasValue: true, Value: false }) == true)
        {
            return false;
        }

        if (creation.MethodSymbol(model) is not { } constructor
            || creation.ArgumentList is not { } argumentList)
        {
            return false;
        }

        var interval = new CSharpMethodParameterLookup(argumentList, constructor).GetAllArgumentParameterMappings()
            .FirstOrDefault(x => x.Symbol.Name == "interval");
        return interval.Node is not null
               && model.GetConstantValue(interval.Node.Expression) is { HasValue: true, Value: IConvertible value }
               && value.ToDouble(null) > 0;
    }

    private static bool IsKnownPositivePeriod(ExpressionSyntax expression, SemanticModel model)
    {
        if (model.GetConstantValue(expression) is { HasValue: true, Value: IConvertible value })
        {
            return value is uint unsigned
                ? unsigned > 0 && unsigned != uint.MaxValue
                : value.ToDouble(null) > 0;
        }

        return expression is InvocationExpressionSyntax invocation
               && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol
               {
                   ContainingType: { } containingType,
                   Name: "FromTicks" or "FromMilliseconds" or "FromSeconds" or "FromMinutes" or "FromHours" or "FromDays",
               }
               && containingType.Is(KnownType.System_TimeSpan)
               && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } argument
               && model.GetConstantValue(argument) is { HasValue: true, Value: IConvertible period }
               && period.ToDouble(null) > 0;
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
