/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpActionShouldDeclareSuccessfulResponseType : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0102";

    private const string MessageFormat = "Declare the concrete successful response type with ProducesResponseType.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is not { } method
            || !GpOpenApiMetadata.IsOpenApiAction(method)
            || GpOpenApiMetadata.IsIgnored(method)
            || GpOpenApiMetadata.UsesApiConvention(method)
            || !ReturnsAbstractActionResult(UnwrapAsync(method.ReturnType))
            || GpOpenApiMetadata.HasConcreteProducedType(method))
        {
            return;
        }

        var responses = GpOpenApiMetadata.ReturnedInvocations(declaration)
            .Where(x => HasSuccessfulPayload(context.Model, x))
            .Where(x => GpOpenApiMetadata.ResponseStatusCode(context.Model, x) is { } statusCode
                        && !GpOpenApiMetadata.HasConcreteResponseTypeForStatus(method, statusCode))
            .ToArray();
        if (responses.Length > 0)
        {
            context.ReportIssue(Rule, declaration.Identifier, responses.ToSecondaryLocations());
        }
    }

    private static ITypeSymbol UnwrapAsync(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } wrapper
        && wrapper.OriginalDefinition.IsAny(KnownType.System_Threading_Tasks_Task_T, KnownType.System_Threading_Tasks_ValueTask_TResult)
            ? wrapper.TypeArguments[0]
            : type;

    private static bool ReturnsAbstractActionResult(ITypeSymbol type) =>
        type.Is(KnownType.Microsoft_AspNetCore_Mvc_IActionResult)
        || type.Is(KnownType.Microsoft_AspNetCore_Http_IResult)
        || type is INamedTypeSymbol { IsGenericType: false, Name: "ActionResult" }
           && type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc";

    private static bool HasSuccessfulPayload(SemanticModel model, InvocationExpressionSyntax invocation) =>
        GpOpenApiMetadata.ResponseStatusCode(model, invocation) is >= 200 and <= 299
        && GpOpenApiMetadata.HasPayload(model, invocation);
}
