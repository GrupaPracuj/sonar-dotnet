/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpClientRegistrationShouldHaveExplicitName : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0090";

    private const string MessageFormat = "Give this HTTP client an explicit name; Juno does not support the default client registration.";
    private const string JunoHttpSenderFactory = "GP.Juno.HttpApiClient.HttpSending.IHttpSenderFactory";
    private const string HttpClientRegistrationExtensions = "Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(JunoHttpSenderFactory) is not null)
            {
                start.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
            }
        });

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "AddHttpClient", Arity: 0 } method
            || method.ContainingType?.ToDisplayString() != HttpClientRegistrationExtensions
            || !IsDefaultClientRegistration(invocation, method))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static bool IsDefaultClientRegistration(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.ReducedFrom is not null
            ? invocation.ArgumentList.Arguments.Count == 0
            : method.IsExtensionMethod && method.Parameters.Length == 1 && invocation.ArgumentList.Arguments.Count == 1;
}
