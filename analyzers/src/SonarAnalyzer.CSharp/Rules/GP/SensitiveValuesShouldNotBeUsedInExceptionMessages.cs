/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SensitiveValuesShouldNotBeUsedInExceptionMessages : SonarDiagnosticAnalyzer
{
    private const string DiagnosticId = "GP0138";
    private const string MessageFormat = "Do not include '{0}' in an exception message - use a non-sensitive identifier instead.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(DiagnosticId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (!ObjectCreationFactory.TryCreate(context.Node, out var creation)
            || creation.ArgumentList is not { } arguments
            || creation.MethodSymbol(context.Model) is not { } constructor
            || !GpJunoTypes.DerivesFrom(constructor.ContainingType, "System.Exception"))
        {
            return;
        }

        var message = new CSharpMethodParameterLookup(arguments, constructor)
            .GetAllArgumentParameterMappings()
            .FirstOrDefault(x => x.Symbol.Name == "message");
        if (message.Node is ArgumentSyntax { Expression: var expression }
            && SensitiveValue(expression, context.Model) is { } sensitive)
        {
            context.ReportIssue(Rule, sensitive.Expression, sensitive.Name);
        }
    }

    private static (string Name, ExpressionSyntax Expression)? SensitiveValue(ExpressionSyntax expression, SemanticModel model)
    {
        expression = (ExpressionSyntax)expression.RemoveParentheses();
        if (SensitiveDirectValue(expression, model) is { } direct)
        {
            return direct;
        }

        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            return interpolated.Contents
                .OfType<InterpolationSyntax>()
                .Select(x => SensitiveValue(x.Expression, model))
                .FirstOrDefault(x => x is not null);
        }

        if (expression is BinaryExpressionSyntax binary
            && binary.IsKind(SyntaxKind.AddExpression)
            && model.GetTypeInfo(binary).ConvertedType?.SpecialType == SpecialType.System_String)
        {
            return SensitiveValue(binary.Left, model) ?? SensitiveValue(binary.Right, model);
        }

        if (expression is InvocationExpressionSyntax invocation
            && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol
            {
                Name: "Format",
                ContainingType.SpecialType: SpecialType.System_String,
            } method)
        {
            return new CSharpMethodParameterLookup(invocation, method)
                .GetAllArgumentParameterMappings()
                .Where(x => x.Symbol.Name is not ("format" or "provider"))
                .Select(x => x.Node)
                .OfType<ArgumentSyntax>()
                .Select(x => SensitiveValue(x.Expression, model))
                .FirstOrDefault(x => x is not null);
        }

        return null;
    }

    private static (string Name, ExpressionSyntax Expression)? SensitiveDirectValue(ExpressionSyntax expression, SemanticModel model)
    {
        foreach (var name in GpLoggingHelper.CandidateNames(expression))
        {
            if (GpIdentifierWords.ContainsSecretWord(name)
                && !DoNotLogSecretLikeValue.IsExcludedType(model, expression))
            {
                return (name, expression);
            }

            if (GpIdentifierWords.ContainsPiiWord(name)
                && !GpJunoTypes.DerivesFrom(model.GetTypeInfo(expression).Type, "System.Exception"))
            {
                return (name, expression);
            }
        }

        return null;
    }
}
