/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JunoInfrastructureHandlersShouldBeProtected : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0128";

    private const string MessageFormat = "Protect Juno infrastructure handlers before UseJuno; the later UseAuthorization middleware does not cover these branch handlers.";
    private const string JunoApplicationBuilderExtensions = "GP.Juno.Hosting.AspNetCore.HostBuilding.ApplicationBuilderJunoExtensions";
    private const string ApplicationBuilder = "Microsoft.AspNetCore.Builder.IApplicationBuilder";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.InvocationExpression);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var useJuno = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(useJuno).Symbol is not IMethodSymbol { Name: "UseJuno" } method
            || (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() != JunoApplicationBuilderExtensions
            || Receiver(useJuno, method) is not { } receiver
            || context.Model.GetSymbolInfo(receiver).Symbol is not { } receiverSymbol
            || !ConfiguresWebHandlers(context.Compilation, context.Model, useJuno, method)
            || !HasLaterAuthorization(context.Model, useJuno, receiverSymbol))
        {
            return;
        }

        context.ReportIssue(Rule, useJuno);
    }

    private static bool HasLaterAuthorization(SemanticModel model, InvocationExpressionSyntax useJuno, ISymbol receiverSymbol) =>
        useJuno.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(x => x.SpanStart > useJuno.Span.End)
            .Any(x => model.GetSymbolInfo(x).Symbol is IMethodSymbol { Name: "UseAuthorization" } method
                      && method.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Builder"
                      && SameExecutionContext(model, useJuno, x)
                      && Receiver(x, method) is { } receiver
                      && Equals(model.GetSymbolInfo(receiver).Symbol, receiverSymbol));

    private static bool ConfiguresWebHandlers(
        Compilation compilation,
        SemanticModel model,
        InvocationExpressionSyntax useJuno,
        IMethodSymbol method)
    {
        var configuration = new CSharpMethodParameterLookup(useJuno, method).GetAllArgumentParameterMappings()
            .Where(x => x.Symbol.Name == "configureJuno")
            .Select(x => x.Node?.Expression)
            .FirstOrDefault(x => x is not null);
        if (configuration is AnonymousFunctionExpressionSyntax lambda)
        {
            return ContainsWebConfiguration(model, lambda);
        }

        return configuration is not null
               && model.GetSymbolInfo(configuration).Symbol is IMethodSymbol configurationMethod
               && configurationMethod.DeclaringSyntaxReferences
                   .Select(x => x.GetSyntax())
                   .OfType<MethodDeclarationSyntax>()
                   .Any(x => ContainsWebConfiguration(compilation.GetSemanticModel(x.SyntaxTree), x));
    }

    private static bool ContainsWebConfiguration(SemanticModel model, SyntaxNode body) =>
        body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(x => model.GetSymbolInfo(x).Symbol as IMethodSymbol)
            .WhereNotNull()
            .Any(x => x.Name is "UseDistributedConfig" or "UseWebApp"
                      && (x.ReducedFrom ?? x).ContainingNamespace?.ToDisplayString().StartsWith("GP.Juno.", StringComparison.Ordinal) == true);

    private static bool SameExecutionContext(
        SemanticModel model,
        InvocationExpressionSyntax left,
        InvocationExpressionSyntax right) =>
        Equals(model.GetEnclosingSymbol(left.SpanStart), model.GetEnclosingSymbol(right.SpanStart));

    private static ExpressionSyntax Receiver(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method.ReducedFrom is not null
            && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } receiver })
        {
            return receiver;
        }

        return new CSharpMethodParameterLookup(invocation, method).GetAllArgumentParameterMappings()
            .Where(x => x.Symbol.Type.ToDisplayString() == ApplicationBuilder)
            .Select(x => x.Node?.Expression)
            .FirstOrDefault(x => x is not null);
    }
}
