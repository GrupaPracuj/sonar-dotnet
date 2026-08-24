/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticClockShouldNotBypassInjectedTimeProvider : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0120";

    private const string MessageFormat = "Use the injected TimeProvider instead of the static clock '{0}'.";
    private const string TimeProviderType = "System.TimeProvider";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> DateTimeMembers = new(StringComparer.Ordinal)
    {
        "Now",
        "UtcNow",
        "Today",
    };
    private static readonly HashSet<string> DateTimeOffsetMembers = new(StringComparer.Ordinal)
    {
        "Now",
        "UtcNow",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);

    private static void AnalyzeMemberAccess(SonarSyntaxNodeReportingContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(access).Symbol is not IPropertySymbol { IsStatic: true } property
            || StaticClockName(property) is not { } clockName
            || context.Model.GetEnclosingSymbol(access.SpanStart) is not { ContainingType: { } containingType } enclosingSymbol
            || enclosingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor }
            || IsTimeProviderInitializer(context.Model, access)
            || IsClockImplementation(containingType)
            || !HasInjectedTimeProvider(containingType))
        {
            return;
        }

        context.ReportIssue(Rule, access, clockName);
    }

    private static bool IsTimeProviderInitializer(SemanticModel model, MemberAccessExpressionSyntax access) =>
        access.FirstAncestorOrSelf<EqualsValueClauseSyntax>()?.Parent switch
        {
            VariableDeclaratorSyntax declaration => model.GetDeclaredSymbol(declaration) is IFieldSymbol field && IsTimeProvider(field.Type),
            PropertyDeclarationSyntax declaration => model.GetDeclaredSymbol(declaration) is IPropertySymbol property && IsTimeProvider(property.Type),
            _ => false,
        };

    private static string StaticClockName(IPropertySymbol property)
    {
        var containingType = property.ContainingType?.ToDisplayString();
        if (containingType == TimeProviderType && property.Name == "System")
        {
            return "TimeProvider.System";
        }

        if (containingType == "System.DateTime" && DateTimeMembers.Contains(property.Name))
        {
            return $"DateTime.{property.Name}";
        }

        return containingType == "System.DateTimeOffset" && DateTimeOffsetMembers.Contains(property.Name)
            ? $"DateTimeOffset.{property.Name}"
            : null;
    }

    private static bool HasInjectedTimeProvider(INamedTypeSymbol containingType)
    {
        var constructors = containingType.InstanceConstructors.Where(x => !x.IsImplicitlyDeclared).ToArray();
        if (constructors.Any(x => x.IsPrimaryConstructor && x.Parameters.Any(parameter => IsTimeProvider(parameter.Type))))
        {
            return true;
        }

        var hasMember = containingType.GetMembers().Any(x => x switch
        {
            IFieldSymbol { IsStatic: false } field => IsTimeProvider(field.Type),
            IPropertySymbol { IsStatic: false } property => IsTimeProvider(property.Type),
            _ => false,
        });
        return hasMember && constructors.Any(x => x.Parameters.Any(parameter => IsTimeProvider(parameter.Type)));
    }

    private static bool IsTimeProvider(ITypeSymbol type) =>
        type.ToDisplayString() == TimeProviderType;

    private static bool IsClockImplementation(INamedTypeSymbol type) =>
        GpJunoTypes.DerivesFrom(type, TimeProviderType)
        || type.Name.EndsWith("TimeProvider", StringComparison.Ordinal)
        || type.Name.EndsWith("Clock", StringComparison.Ordinal);
}
