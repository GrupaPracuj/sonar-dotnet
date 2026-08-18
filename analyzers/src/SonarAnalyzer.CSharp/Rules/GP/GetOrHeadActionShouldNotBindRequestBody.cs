/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GetOrHeadActionShouldNotBindRequestBody : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0094";

    private const string MessageFormat = "Remove '[FromBody]' from this {0} action; request-body semantics are not defined for this HTTP method.";
    private const string FromBodyAttribute = "Microsoft.AspNetCore.Mvc.FromBodyAttribute";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly string[] MinimalApiMapMethods = ["MapGet", "MapMethods"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterNodeAction(AnalyzeMinimalApiParameter, SyntaxKind.Parameter);
    }

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not MethodDeclarationSyntax declaration
            || context.Model.GetDeclaredSymbol(declaration) is not { } method
            || !method.IsControllerActionMethod()
            || HttpMethod(method) is not { } httpMethod)
        {
            return;
        }

        for (var i = 0; i < Math.Min(method.Parameters.Length, declaration.ParameterList.Parameters.Count); i++)
        {
            if (method.Parameters[i].GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == FromBodyAttribute))
            {
                context.ReportIssue(Rule, declaration.ParameterList.Parameters[i], httpMethod);
            }
        }
    }

    private static void AnalyzeMinimalApiParameter(SonarSyntaxNodeReportingContext context)
    {
        var parameter = (ParameterSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(parameter) is not IParameterSymbol symbol
            || !symbol.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == FromBodyAttribute)
            || !GpMinimalApi.TryGetInlineHandler(parameter, context.Model, MinimalApiMapMethods, out _, out var mapInvocation, out var mapMethod, out _)
            || GpMinimalApi.HttpMethods(mapInvocation, mapMethod, context.Model).FirstOrDefault(x => x is "GET" or "HEAD") is not { } httpMethod)
        {
            return;
        }

        context.ReportIssue(Rule, parameter, httpMethod);
    }

    private static string HttpMethod(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass.Is(KnownType.Microsoft_AspNetCore_Mvc_HttpGetAttribute))
            {
                return "GET";
            }

            if (attribute.AttributeClass.Is(KnownType.Microsoft_AspNetCore_Mvc_HttpHeadAttribute))
            {
                return "HEAD";
            }

            if (attribute.AttributeClass.Is(KnownType.Microsoft_AspNetCore_Mvc_AcceptVerbsAttribute)
                && AcceptVerb(attribute) is { } acceptedVerb)
            {
                return acceptedVerb;
            }
        }

        return null;
    }

    private static string AcceptVerb(AttributeData attribute) =>
        attribute.ConstructorArguments
            .SelectMany(Flatten)
            .Where(x => x.Value is string)
            .Select(x => ((string)x.Value).ToUpperInvariant())
            .FirstOrDefault(x => x is "GET" or "HEAD");

    private static IEnumerable<TypedConstant> Flatten(TypedConstant value) =>
        value.Kind == TypedConstantKind.Array ? value.Values.SelectMany(Flatten) : [value];
}
