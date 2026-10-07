/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// AI005 / GP0143. Reports ChatClientAgent names that are missing or provably change between process runs.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AiAgentShouldHaveStableName : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0143";

    private const string MessageFormat = "Give this ChatClientAgent a stable, deterministic name instead of an unstable or missing one.";
    private const string NameParameter = "name";
    private const string NameProperty = "Name";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private enum NameStability
    {
        Stable,
        Unstable,
        Unknown,
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (!ObjectCreationFactory.TryCreate(context.Node, out var creation)
            || creation.TypeSymbol(context.Model) is not { } type
            || !GpAi.IsChatClientAgent(type))
        {
            return;
        }

        if (TryFindNameInOptions(context.Model, creation, out var optionsName))
        {
            // The name is configured through a ChatClientAgentOptions argument: report only what is provable about it.
            if (optionsName is null)
            {
                context.ReportIssue(Rule, creation.Expression);
            }
            else if (ClassifyStability(context.Model, optionsName) == NameStability.Unstable)
            {
                context.ReportIssue(Rule, optionsName);
            }

            return;
        }

        if (TakesOptions(context.Model, creation))
        {
            return; // FN: options come from a parameter, field or method call - the name is not visible here.
        }

        var nameExpression = FindNameExpression(context.Model, creation);
        if (nameExpression is null)
        {
            context.ReportIssue(Rule, creation.Expression);
        }
        else if (ClassifyStability(context.Model, nameExpression) == NameStability.Unstable)
        {
            context.ReportIssue(Rule, nameExpression);
        }
    }

    private static bool TakesOptions(SemanticModel model, IObjectCreation creation) =>
        creation.MethodSymbol(model) is { } ctor && ctor.Parameters.Any(x => x.Type.Name == "ChatClientAgentOptions");

    // True when the options argument is a local or inline object that can be inspected; 'name' is then its effective
    // Name (initializer or a later "options.Name = ...") or null when it is provably never set.
    private static bool TryFindNameInOptions(SemanticModel model, IObjectCreation creation, out ExpressionSyntax name)
    {
        name = null;
        if (creation.MethodSymbol(model) is not { } ctor
            || ctor.Parameters.FirstOrDefault(x => x.Type.Name == "ChatClientAgentOptions") is not { } parameter)
        {
            return false;
        }

        var arguments = creation.ArgumentList?.Arguments ?? default;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var candidate = argument.NameColon is { Name.Identifier.ValueText: var argName }
                ? ctor.Parameters.FirstOrDefault(x => x.Name == argName)
                : (i < ctor.Parameters.Length ? ctor.Parameters[i] : null);
            if (candidate is not null && candidate.Equals(parameter))
            {
                return GpAi.TryResolveEffectivePropertyValue(model, argument.Expression, creation.Expression, NameProperty, out name);
            }
        }

        return false;
    }

    private static ExpressionSyntax FindNameExpression(SemanticModel model, IObjectCreation creation)
    {
        if (creation.MethodSymbol(model) is { } ctor
            && ctor.Parameters.FirstOrDefault(x => string.Equals(x.Name, NameParameter, StringComparison.OrdinalIgnoreCase)) is { } parameter)
        {
            var arguments = creation.ArgumentList?.Arguments ?? default;
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = arguments[i];
                var candidate = argument.NameColon is { Name.Identifier.ValueText: var argName }
                    ? ctor.Parameters.FirstOrDefault(x => x.Name == argName)
                    : (i < ctor.Parameters.Length ? ctor.Parameters[i] : null);
                if (candidate is not null && candidate.Equals(parameter))
                {
                    return argument.Expression;
                }
            }
        }

        return creation.InitializerExpressions?.OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(x => x.Left is IdentifierNameSyntax { Identifier.ValueText: { } name } && name == NameProperty)
            ?.Right;
    }

    private static NameStability ClassifyStability(SemanticModel model, ExpressionSyntax expression)
    {
        expression = (ExpressionSyntax)expression.RemoveParentheses();
        return expression switch
        {
            LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } => NameStability.Stable,
            LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression } => NameStability.Unstable,
            InterpolatedStringExpressionSyntax interpolated => ClassifyInterpolated(model, interpolated),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } binary => ClassifyConcat(model, binary),
            InvocationExpressionSyntax invocation => ClassifyInvocation(model, invocation),
            IdentifierNameSyntax or MemberAccessExpressionSyntax => ClassifyReference(model, expression),
            _ => NameStability.Unknown,
        };
    }

    private static NameStability ClassifyInvocation(SemanticModel model, InvocationExpressionSyntax invocation) =>
        ContainsUnstableCall(model, invocation) ? NameStability.Unstable : NameStability.Unknown;

    private static NameStability ClassifyConcat(SemanticModel model, BinaryExpressionSyntax binary)
    {
        var left = ClassifyStability(model, binary.Left);
        var right = ClassifyStability(model, binary.Right);
        if (left == NameStability.Unstable || right == NameStability.Unstable)
        {
            return NameStability.Unstable;
        }

        return left == NameStability.Stable && right == NameStability.Stable ? NameStability.Stable : NameStability.Unknown;
    }

    private static NameStability ClassifyInterpolated(SemanticModel model, InterpolatedStringExpressionSyntax interpolated)
    {
        var sawNonLiteral = false;
        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolationSyntax interpolation)
            {
                if (ContainsUnstableCall(model, interpolation.Expression))
                {
                    return NameStability.Unstable;
                }

                if (ClassifyStability(model, interpolation.Expression) != NameStability.Stable)
                {
                    sawNonLiteral = true;
                }
            }
        }

        return sawNonLiteral ? NameStability.Unknown : NameStability.Stable;
    }

    private static NameStability ClassifyReference(SemanticModel model, ExpressionSyntax expression)
    {
        if (model.GetSymbolInfo(expression).Symbol is not { } symbol)
        {
            return NameStability.Unknown;
        }

        switch (symbol)
        {
            case IFieldSymbol { IsConst: true }:
            case ILocalSymbol { IsConst: true }:
                return NameStability.Stable;
            case IFieldSymbol { IsStatic: true, IsReadOnly: true } field:
                return FieldInitializerStability(model.Compilation, field);
            case ILocalSymbol:
                var resolved = GpAi.ResolveLocalValue(model, expression);
                return ReferenceEquals(resolved, expression) ? NameStability.Unknown : ClassifyStability(model, resolved);
            default:
                return NameStability.Unknown;
        }
    }

    private static NameStability FieldInitializerStability(Compilation compilation, IFieldSymbol field)
    {
        var declarator = field.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        if (declarator?.Initializer?.Value is not { } initializerValue)
        {
            return NameStability.Unknown;
        }

        return ClassifyStability(compilation.GetSemanticModel(declarator.SyntaxTree), initializerValue);
    }

    private static bool ContainsUnstableCall(SemanticModel model, ExpressionSyntax expression) =>
        expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(x => IsGuidNewGuid(model, x))
        || expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Any(x => IsUnstableDateTimeNow(model, x));

    private static bool IsGuidNewGuid(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "NewGuid", ContainingType: { } containingType }
        && containingType.ToDisplayString() == "System.Guid";

    private static bool IsUnstableDateTimeNow(SemanticModel model, MemberAccessExpressionSyntax memberAccess) =>
        memberAccess.Name.Identifier.ValueText is "Now" or "UtcNow" or "Today"
        && model.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol { ContainingType: { } containingType }
        && containingType.ToDisplayString() is "System.DateTime" or "System.DateTimeOffset";
}
