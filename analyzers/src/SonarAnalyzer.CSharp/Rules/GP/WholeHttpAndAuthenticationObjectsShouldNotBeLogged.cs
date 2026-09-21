/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WholeHttpAndAuthenticationObjectsShouldNotBeLogged : SonarDiagnosticAnalyzer
{
    private const string DiagnosticId = "GP0136";
    private const string MessageFormat = "Do not log the whole '{0}' object - log only explicitly selected, non-sensitive fields.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(DiagnosticId, MessageFormat);
    private static readonly HashSet<string> SensitiveTypeNames = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Authentication.AuthenticationProperties",
        "Microsoft.AspNetCore.Authentication.AuthenticationTicket",
        "Microsoft.AspNetCore.Http.HttpContext",
        "Microsoft.AspNetCore.Http.HttpRequest",
        "Microsoft.AspNetCore.Http.HttpResponse",
        "Microsoft.AspNetCore.Http.IFormCollection",
        "Microsoft.AspNetCore.Http.IHeaderDictionary",
        "Microsoft.AspNetCore.Http.IQueryCollection",
        "Microsoft.AspNetCore.Http.IRequestCookieCollection",
        "Microsoft.AspNetCore.Http.QueryString",
        "System.Security.Claims.ClaimsIdentity",
        "System.Security.Claims.ClaimsPrincipal",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList is not { } argumentList || !GpLoggingHelper.IsLoggingCall(context.Model, invocation))
        {
            return;
        }

        foreach (var argument in argumentList.Arguments)
        {
            if (SensitiveTypeInLogArgument(context.Model, argument.Expression) is { } type)
            {
                context.ReportIssue(Rule, argument, type.Name);
                return;
            }
        }
    }

    private static ITypeSymbol SensitiveTypeInLogArgument(SemanticModel model, ExpressionSyntax expression)
    {
        if (SensitiveType(model.GetTypeInfo(expression).Type) is { } direct)
        {
            return direct;
        }

        return expression.RemoveParentheses() switch
        {
            InterpolatedStringExpressionSyntax interpolated => interpolated.Contents
                .OfType<InterpolationSyntax>()
                .Select(x => SensitiveType(model.GetTypeInfo(x.Expression).Type))
                .FirstOrDefault(x => x is not null),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression)
                && model.GetTypeInfo(binary).ConvertedType?.SpecialType == SpecialType.System_String =>
                SensitiveTypeInLogArgument(model, binary.Left) ?? SensitiveTypeInLogArgument(model, binary.Right),
            _ => null,
        };
    }

    private static ITypeSymbol SensitiveType(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SensitiveTypeNames.Contains(current.ToDisplayString()))
            {
                return current;
            }
        }

        return type?.AllInterfaces.FirstOrDefault(x => SensitiveTypeNames.Contains(x.ToDisplayString()));
    }
}
