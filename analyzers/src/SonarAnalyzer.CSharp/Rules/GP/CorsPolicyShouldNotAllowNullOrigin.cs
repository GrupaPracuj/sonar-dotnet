/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CorsPolicyShouldNotAllowNullOrigin : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0095";

    private const string MessageFormat = "Remove the 'null' origin from this CORS policy and allow only explicit trusted origins.";
    private const string CorsPolicyBuilder = "Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder";
    private const string HeaderDictionaryExtensions = "Microsoft.AspNetCore.Http.HeaderDictionaryExtensions";
    private const string HeaderDictionary = "Microsoft.AspNetCore.Http.IHeaderDictionary";
    private const string AllowOriginHeader = "Access-Control-Allow-Origin";
    private const string NullOrigin = "null";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
        context.RegisterNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (method is { Name: "WithOrigins" }
            && method.ContainingType?.ToDisplayString() == CorsPolicyBuilder
            && invocation.ArgumentList.Arguments.Any(x => IsNullOrigin(x.Expression, context.Model)))
        {
            context.ReportIssue(Rule, invocation);
        }
        else if (IsHeaderWrite(invocation, method, context.Model))
        {
            context.ReportIssue(Rule, invocation);
        }
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model)?.ToDisplayString() == CorsPolicyBuilder
            && creation.ArgumentList?.Arguments.Any(x => IsNullOrigin(x.Expression, context.Model)) == true)
        {
            context.ReportIssue(Rule, creation.Expression);
        }
    }

    private static void AnalyzeAssignment(SonarSyntaxNodeReportingContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Left is ElementAccessExpressionSyntax { Expression: { } receiver } elementAccess
            && elementAccess.ArgumentList.Arguments.Count == 1
            && elementAccess.ArgumentList.Arguments[0].Expression is { } headerExpression
            && GpJunoTypes.Implements(context.Model.GetTypeInfo(receiver).Type, HeaderDictionary)
            && IsAllowOriginHeader(headerExpression, context.Model)
            && IsNullOrigin(assignment.Right, context.Model))
        {
            context.ReportIssue(Rule, assignment);
        }
    }

    private static bool IsHeaderWrite(InvocationExpressionSyntax invocation, IMethodSymbol method, SemanticModel model)
    {
        if (method is { Name: "Append" } && method.ContainingType?.ToDisplayString() == HeaderDictionaryExtensions)
        {
            var lookup = new CSharpMethodParameterLookup(invocation, method);
            return HeaderReceiver(invocation, method, lookup, model) is not null
                   && lookup.TryGetSyntax("key", out var keys)
                   && keys.Length == 1
                   && IsAllowOriginHeader((ExpressionSyntax)keys[0], model)
                   && lookup.TryGetSyntax("value", out var values)
                   && values.Length == 1
                   && IsNullOrigin((ExpressionSyntax)values[0], model);
        }

        return invocation.ArgumentList.Arguments.Count == 2
               && IsAllowOriginHeader(invocation.ArgumentList.Arguments[0].Expression, model)
               && IsNullOrigin(invocation.ArgumentList.Arguments[1].Expression, model)
               && method.Name == "Add"
               && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } receiver }
               && GpJunoTypes.Implements(model.GetTypeInfo(receiver).Type, HeaderDictionary);
    }

    private static ExpressionSyntax HeaderReceiver(InvocationExpressionSyntax invocation, IMethodSymbol method, CSharpMethodParameterLookup lookup, SemanticModel model)
    {
        ExpressionSyntax receiver;
        if (method.ReducedFrom is not null)
        {
            receiver = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        }
        else if (method.Parameters.FirstOrDefault(x => x.Name == "headers" && GpJunoTypes.Implements(x.Type, HeaderDictionary)) is { } receiverParameter
                 && lookup.TryGetSyntax(receiverParameter, out var receivers)
                 && receivers.Length == 1)
        {
            receiver = (ExpressionSyntax)receivers[0];
        }
        else
        {
            receiver = null;
        }

        return receiver is not null && GpJunoTypes.Implements(model.GetTypeInfo(receiver).Type, HeaderDictionary)
            ? receiver
            : null;
    }

    private static bool IsAllowOriginHeader(ExpressionSyntax expression, SemanticModel model) =>
        model.GetConstantValue(expression) is { HasValue: true, Value: AllowOriginHeader };

    private static bool IsNullOrigin(ExpressionSyntax expression, SemanticModel model)
    {
        if (model.GetConstantValue(expression) is { HasValue: true, Value: NullOrigin })
        {
            return true;
        }

        if (expression is InterpolatedStringExpressionSyntax interpolation)
        {
            return interpolation.FindStringConstant(model) == NullOrigin;
        }

        if (ObjectCreationFactory.TryCreate(expression, out var creation))
        {
            return creation.ArgumentList?.Arguments.Any(x => IsNullOrigin(x.Expression, model)) == true;
        }

        return expression switch
        {
            ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions.Any(x => IsNullOrigin(x, model)),
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions.Any(x => IsNullOrigin(x, model)),
            _ => false,
        };
    }
}
