/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using Microsoft.CodeAnalysis.Text;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PersonalDataShouldNotBePassedInUrl : SonarDiagnosticAnalyzer
{
    private const string DiagnosticId = "GP0135";
    private const string MessageFormat = "URL parameter '{0}' looks like it carries personal data - it will end up in server logs, browser history and proxy caches.";
    private const string FromQueryAttribute = "Microsoft.AspNetCore.Mvc.FromQueryAttribute";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(DiagnosticId, MessageFormat);
    private static readonly HashSet<string> MinimalApiMapMethods = new(StringComparer.Ordinal)
    {
        "Map",
        "MapDelete",
        "MapGet",
        "MapMethods",
        "MapPatch",
        "MapPost",
        "MapPut",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
        context.RegisterNodeAction(AnalyzeMinimalApiRoute, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeQueryParameter, SyntaxKind.Parameter);
    }

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        if (context.Model.GetDeclaredSymbol(context.Node) is IMethodSymbol method)
        {
            AnalyzeAttributes(context, method.GetAttributes());
        }
    }

    private static void AnalyzeClass(SonarSyntaxNodeReportingContext context)
    {
        if (context.Model.GetDeclaredSymbol(context.Node) is INamedTypeSymbol type)
        {
            AnalyzeAttributes(context, type.GetAttributes());
        }
    }

    private static void AnalyzeMinimalApiRoute(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpMinimalApi.TryGetMapMethod(invocation, context.Model, MinimalApiMapMethods, out _, out var routeTemplate)
            && routeTemplate is { Length: > 0 })
        {
            AnalyzeRouteParameters(context, invocation, routeTemplate);
        }
    }

    private static void AnalyzeQueryParameter(SonarSyntaxNodeReportingContext context)
    {
        var parameterSyntax = (ParameterSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(parameterSyntax) is not IParameterSymbol parameter
            || parameter.GetAttributes().FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == FromQueryAttribute) is not { } attribute)
        {
            return;
        }

        var queryName = attribute.NamedArguments.FirstOrDefault(x => x.Key == "Name").Value.Value as string ?? parameter.Name;
        if (GpIdentifierWords.ContainsPiiValueWord(queryName))
        {
            context.ReportIssue(Rule, parameterSyntax.Identifier.GetLocation(), queryName);
        }
    }

    private static void AnalyzeAttributes(SonarSyntaxNodeReportingContext context, ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeRouteTemplate is { Length: > 0 } template
                && attribute.ApplicationSyntaxReference?.GetSyntax() is { } attributeSyntax)
            {
                AnalyzeRouteParameters(context, attributeSyntax, template);
            }
        }
    }

    private static void AnalyzeRouteParameters(SonarSyntaxNodeReportingContext context, SyntaxNode routeSyntax, string template)
    {
        foreach (var (segment, offset) in RouteNamingConventions.Segments(template))
        {
            if (IsParameter(segment)
                && ParameterName(segment) is { Length: > 0 } parameterName
                && GpIdentifierWords.ContainsPiiValueWord(parameterName))
            {
                context.ReportIssue(Rule, TemplateLocation(routeSyntax, template, offset + 1, parameterName.Length), parameterName);
            }
        }
    }

    private static bool IsParameter(string segment) =>
        segment[0] == '{' && segment.EndsWith("}", StringComparison.Ordinal);

    private static string ParameterName(string segment)
    {
        var name = segment.Substring(1, segment.Length - 2).TrimStart('*');
        var boundary = name.IndexOfAny(new[] { ':', '=' });
        name = boundary >= 0 ? name.Substring(0, boundary) : name;
        return name.TrimEnd('?');
    }

    private static Location TemplateLocation(SyntaxNode routeSyntax, string template, int offset, int length)
    {
        if (routeSyntax.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .FirstOrDefault(x => x.IsKind(SyntaxKind.StringLiteralExpression) && x.Token.ValueText == template) is { Token: var token }
            && token.Text.Length == template.Length + 2)
        {
            return Location.Create(token.SyntaxTree, new TextSpan(token.SpanStart + 1 + offset, length));
        }

        return routeSyntax.GetLocation();
    }
}
