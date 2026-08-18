/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigurationShouldBeBoundToTypedClass : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0039";

    private const string MessageFormat = "Bind configuration to a typed class instead of reading it by key.";

    private const string ConfigurationInterface = "Microsoft.Extensions.Configuration.IConfiguration";
    private const string ServiceCollectionInterface = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string WebApplicationBuilderType = "Microsoft.AspNetCore.Builder.WebApplicationBuilder";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeElementAccess, SyntaxKind.ElementAccessExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // configuration["Orders:BaseUrl"]
    private static void AnalyzeElementAccess(SonarSyntaxNodeReportingContext context)
    {
        var elementAccess = (ElementAccessExpressionSyntax)context.Node;
        if (IsConfiguration(context.Model.GetTypeInfo(elementAccess.Expression).Type)
            && !IsServiceRegistrationMethod(context.Model, elementAccess))
        {
            context.ReportIssue(Rule, elementAccess);
        }
    }

    // configuration.GetValue<int>("Orders:Timeout") - typed, but still one value looked up by key.
    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "GetValue" } method)
        {
            return;
        }

        // GetValue is an extension method on IConfiguration, so the receiver carries the type.
        if ((IsConfiguration(method.ReceiverType) || (method.Parameters.Length > 0 && IsConfiguration(method.Parameters[0].Type)))
            && !IsServiceRegistrationMethod(context.Model, invocation))
        {
            context.ReportIssue(Rule, invocation);
        }
    }

    // GetSection(...) is not reported: it is how a section is selected before being bound with Get<T>()/Bind(...),
    // which is the pattern this rule steers towards.
    private static bool IsConfiguration(ITypeSymbol type) =>
        GpJunoTypes.Implements(type, ConfigurationInterface);

    // The composition root is where configuration is intentionally converted into concrete dependencies. Reading
    // a single value while registering a framework service (for example an EF connection string) does not leak the
    // configuration bag into runtime application code.
    private static bool IsServiceRegistrationMethod(SemanticModel model, SyntaxNode node) =>
        model.GetEnclosingSymbol(node.SpanStart) is IMethodSymbol method
        && (IsServiceCollection(method.ReturnType)
            || method.Parameters.Any(x => IsServiceCollection(x.Type))
            || IsWebApplicationBuilderRegistrationMethod(model, method));

    private static bool IsServiceCollection(ITypeSymbol type) =>
        type?.ToDisplayString() == ServiceCollectionInterface;

    // Modern ASP.NET composition roots commonly extend WebApplicationBuilder and configure framework services through
    // builder.Services. Keep the exemption tied to that concrete setup shape rather than exempting every method that
    // merely receives a builder and could still leak keyed configuration into runtime logic.
    private static bool IsWebApplicationBuilderRegistrationMethod(SemanticModel model, IMethodSymbol method)
    {
        if (!method.IsExtensionMethod
            || method.Parameters.FirstOrDefault() is not { Type: { } receiverType } receiver
            || receiverType.ToDisplayString() != WebApplicationBuilderType)
        {
            return false;
        }

        return method.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .Where(x => x.SyntaxTree == model.SyntaxTree)
            .SelectMany(x => x.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            .Any(x => x.Name.Identifier.ValueText == "Services"
                      && x.Expression is IdentifierNameSyntax identifier
                      && receiver.Equals(model.GetSymbolInfo(identifier).Symbol));
    }
}
