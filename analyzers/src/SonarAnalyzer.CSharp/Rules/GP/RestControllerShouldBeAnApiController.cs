/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RestControllerShouldBeAnApiController : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0132";

    private const string MessageFormat = "Derive '{0}' from ControllerBase or mark it [ApiController]; it serves REST but is declared as a view-rendering controller.";
    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> ViewFactories = new(StringComparer.Ordinal) { "View", "PartialView", "ViewComponent" };
    private static readonly HashSet<string> ViewState = new(StringComparer.Ordinal) { "ViewBag", "ViewData" };
    private static readonly HashSet<string> ViewResults = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Mvc.ViewResult",
        "Microsoft.AspNetCore.Mvc.PartialViewResult",
        "Microsoft.AspNetCore.Mvc.ViewComponentResult",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.ClassDeclaration);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol { IsAbstract: false } type
            || !type.IsControllerType
            || type.IsCoreApiController
            || !type.DerivesFrom(KnownType.Microsoft_AspNetCore_Mvc_Controller))
        {
            return;
        }

        var actions = type.GetMembers().OfType<IMethodSymbol>().Where(x => x.IsControllerActionMethod).ToList();
        if (actions.Count > 0 && actions.Any(ServesRest) && !RendersViews(context, declaration, actions))
        {
            context.ReportIssue(Rule, declaration.Identifier, type.Name);
        }
    }

    // The evidence has to be positive and specific to a machine-readable API: a media type the action commits to, or a
    // response body it declares by type. An action that merely returns Ok() or NoContent() proves nothing - a
    // view-rendering controller does that too, for the JavaScript of the page it serves.
    private static bool ServesRest(IMethodSymbol action) =>
        action.AttributesWithInherited
            .Concat(action.ContainingType.AttributesWithInherited)
            .Any(x => x.AttributeClass.DerivesFrom(KnownType.Microsoft_AspNetCore_Mvc_ProducesAttribute)
                      || x.AttributeClass.DerivesFrom(KnownType.Microsoft_AspNetCore_Mvc_ProducesAttribute_T)
                      || (GpOpenApiMetadata.IsResponseAttribute(x) && GpOpenApiMetadata.ResponseType(x) is not null));

    private static bool RendersViews(SonarSyntaxNodeReportingContext context, ClassDeclarationSyntax declaration, List<IMethodSymbol> actions) =>
        actions.Any(x => ViewResults.Contains(ReturnedType(x).ToDisplayString()))
        || declaration.DescendantNodes().Any(x => IsViewMember(context, x));

    private static ITypeSymbol ReturnedType(IMethodSymbol action) =>
        action.ReturnType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } awaitable
        && awaitable.ConstructedFrom.Is(KnownType.System_Threading_Tasks_Task_T)
            ? awaitable.TypeArguments[0]
            : action.ReturnType;

    private static bool IsViewMember(SonarSyntaxNodeReportingContext context, SyntaxNode node) =>
        node switch
        {
            InvocationExpressionSyntax invocation =>
                context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                && ViewFactories.Contains(method.Name)
                && IsControllerMember(method),
            IdentifierNameSyntax identifier when ViewState.Contains(identifier.Identifier.ValueText) =>
                context.Model.GetSymbolInfo(identifier).Symbol is IPropertySymbol property && IsControllerMember(property),
            _ => false,
        };

    private static bool IsControllerMember(ISymbol member) =>
        member.ContainingType.Is(KnownType.Microsoft_AspNetCore_Mvc_Controller)
        || member.ContainingType.DerivesFrom(KnownType.Microsoft_AspNetCore_Mvc_Controller);
}
