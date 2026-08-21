/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using System.Collections.Concurrent;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CreatedAtActionShouldUseRoutableActionName : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0113";

    private const string MessageFormat = "This Async-suffixed action name is suppressed by MVC; use a named route with CreatedAtRoute instead.";
    private const string MvcOptionsType = "Microsoft.AspNetCore.Mvc.MvcOptions";
    private const string SuppressAsyncSuffixProperty = "SuppressAsyncSuffixInActionNames";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var candidates = new ConcurrentBag<Location>();
            var options = new OptionAssignments();
            start.RegisterNodeAction(c => AnalyzeInvocation(c, candidates), SyntaxKind.InvocationExpression);
            start.RegisterNodeAction(c => AnalyzeOptionAssignment(c, options), SyntaxKind.SimpleAssignmentExpression);
            start.RegisterCompilationEndAction(c => Report(c, candidates, options));
        });

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context, ConcurrentBag<Location> candidates)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!GpMvcResults.TryGetResultMethod(context.Model, invocation, out var method)
            || method.Name != "CreatedAtAction")
        {
            return;
        }

        var lookup = new CSharpMethodParameterLookup(invocation, method);
        if (!lookup.TryGetSyntax("actionName", out var arguments)
            || arguments.Length != 1
            || arguments[0] is not ExpressionSyntax actionName
            || !TryGetNameofTarget(actionName, out var target)
            || TargetMethods(context.Model, target) is not { IsEmpty: false } targetMethods
            || !targetMethods.All(IsSuppressedAsyncAction))
        {
            return;
        }

        candidates.Add(actionName.GetLocation());
    }

    private static bool TryGetNameofTarget(ExpressionSyntax expression, out ExpressionSyntax target)
    {
        target = null;
        if (expression.RemoveParentheses() is InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
            } nameofInvocation
            && nameofInvocation.ArgumentList.Arguments.Count == 1)
        {
            target = nameofInvocation.ArgumentList.Arguments[0].Expression;
            return true;
        }

        return false;
    }

    private static ImmutableArray<IMethodSymbol> TargetMethods(SemanticModel model, ExpressionSyntax target)
    {
        var symbolInfo = model.GetSymbolInfo(target);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return ImmutableArray.Create(method);
        }

        return symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToImmutableArray();
    }

    private static bool IsSuppressedAsyncAction(IMethodSymbol method) =>
        method.Name.EndsWith("Async", StringComparison.Ordinal)
        && method.IsControllerActionMethod
        && !method.GetAttributes().Any(x => HasMatchingActionName(x, method.Name));

    private static bool HasMatchingActionName(AttributeData attribute, string methodName) =>
        attribute.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Mvc.ActionNameAttribute"
        && attribute.ConstructorArguments.Length == 1
        && attribute.ConstructorArguments[0].Value is string actionName
        && actionName == methodName;

    private static void AnalyzeOptionAssignment(SonarSyntaxNodeReportingContext context, OptionAssignments options)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol
            {
                Name: SuppressAsyncSuffixProperty,
                ContainingType: { } containingType,
                Type.SpecialType: SpecialType.System_Boolean,
            }
            || containingType.ToDisplayString() != MvcOptionsType
            || context.Model.GetConstantValue(assignment.Right) is not { HasValue: true, Value: bool value })
        {
            return;
        }

        if (value)
        {
            Interlocked.Exchange(ref options.HasTrue, 1);
        }
        else
        {
            Interlocked.Exchange(ref options.HasFalse, 1);
        }
    }

    private static void Report(SonarCompilationReportingContext context, IEnumerable<Location> candidates, OptionAssignments options)
    {
        if (options.HasFalse != 0)
        {
            return;
        }

        foreach (var location in candidates)
        {
            context.ReportIssue(CSharpGeneratedCodeRecognizer.Instance, Rule, location);
        }
    }

    private sealed class OptionAssignments
    {
        public int HasTrue;
        public int HasFalse;
    }
}
