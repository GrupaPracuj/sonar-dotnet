/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SensitiveDiagnosticsShouldBeDevelopmentOnly : SonarDiagnosticAnalyzer
{
    private const string DiagnosticId = "GP0137";
    private const string MessageFormat = "Enable '{0}' only in a development environment.";
    private const string DbContextOptionsBuilder = "Microsoft.EntityFrameworkCore.DbContextOptionsBuilder";
    private const string DatabaseDeveloperPageExtensions = "Microsoft.Extensions.DependencyInjection.DatabaseDeveloperPageExceptionFilterServiceExtensions";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(DiagnosticId, MessageFormat);
    private static readonly HashSet<string> SensitiveMethods = new(StringComparer.Ordinal)
    {
        "EnableDetailedErrors",
        "EnableSensitiveDataLogging",
    };
    private static readonly HashSet<string> DevelopmentCheckTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Hosting.HostingEnvironmentExtensions",
        "Microsoft.Extensions.Hosting.HostEnvironmentEnvExtensions",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !EnablesSensitiveDiagnostics(invocation, method, context.Model)
            || IsDevelopmentOnly(invocation, context.Model))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.Name);
    }

    private static bool EnablesSensitiveDiagnostics(InvocationExpressionSyntax invocation, IMethodSymbol method, SemanticModel model)
    {
        var original = method.ReducedFrom ?? method;
        if (original.ContainingType?.ToDisplayString() == DatabaseDeveloperPageExtensions
            && method.Name == "AddDatabaseDeveloperPageExceptionFilter")
        {
            return true;
        }

        if (original.ContainingType?.OriginalDefinition.ToDisplayString() != DbContextOptionsBuilder
            || !SensitiveMethods.Contains(method.Name))
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        return arguments.Count == 0
            || arguments.Count == 1 && model.GetConstantValue(arguments[0].Expression) is { HasValue: true, Value: true };
    }

    // Match S4507's deliberately conservative behavior: if the enclosing method performs a real framework
    // IsDevelopment check, do not second-guess how that result is threaded through local control flow.
    private static bool IsDevelopmentOnly(SyntaxNode node, SemanticModel model) =>
        node.Ancestors().OfType<ClassDeclarationSyntax>().Any(x => x.Identifier.ValueText == "StartupDevelopment")
        || EnclosingExecutable(node).DescendantNodes().OfType<InvocationExpressionSyntax>().Any(x =>
            model.GetSymbolInfo(x).Symbol is IMethodSymbol method
            && method.Name == "IsDevelopment"
            && DevelopmentCheckTypes.Contains((method.ReducedFrom ?? method).ContainingType?.ToDisplayString() ?? string.Empty));

    private static SyntaxNode EnclosingExecutable(SyntaxNode node) =>
        node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault()
        ?? node.SyntaxTree.GetRoot();
}
