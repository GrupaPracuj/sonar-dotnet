/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotCreateFrameworkHttpClient : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0033";

    private const string MessageFormat = "Obtain the HTTP client from Juno (IHttpClientBuilder.Service(...)) instead of creating '{0}' directly.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> ClientTypes = new(StringComparer.Ordinal)
    {
        "System.Net.Http.HttpClient",
        "System.Net.WebClient",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (!ObjectCreationFactory.TryCreate(context.Node, out var creation)
            || creation.TypeSymbol(context.Model) is not { } type
            || !ClientTypes.Contains(type.ToDisplayString())
            || IsCoveredByControllerReuseRule(context, type))
        {
            return;
        }

        context.ReportIssue(Rule, creation.Expression, type.Name);
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return;
        }

        var containingType = method.ContainingType?.ToDisplayString() ?? string.Empty;
        if ((method.Name == "CreateClient" && containingType == "System.Net.Http.IHttpClientFactory")
            || (method.Name == "Create" && containingType == "System.Net.WebRequest"))
        {
            context.ReportIssue(Rule, invocation, $"{method.ContainingType.Name}.{method.Name}");
        }
    }

    // S6962 reports "new HttpClient()" in the body of a controller action, so that shape is left to it. A client
    // created in a controller's field initializer or constructor is not reported by S6962 and is reported here.
    private static bool IsCoveredByControllerReuseRule(SonarSyntaxNodeReportingContext context, ITypeSymbol createdType)
    {
        if (createdType.ToDisplayString() != "System.Net.Http.HttpClient"
            || context.Node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is not { } typeDeclaration
            || context.Model.GetDeclaredSymbol(typeDeclaration) is not { } enclosingType
            || !enclosingType.IsControllerType())
        {
            return false;
        }

        return context.Node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method
               && context.Model.GetDeclaredSymbol(method) is { DeclaredAccessibility: Accessibility.Public };
    }
}
