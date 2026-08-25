/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EndpointsShouldNotReturnEntities : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0045";

    private const string EntityMessageFormat = "'{0}' is a database entity - return a response contract instead.";

    // Left empty, the base-type path was dead and the rule fell back to EF attributes or DbSet membership,
    // both of which need the entity in the same compilation as the contract. These are the names the
    // pattern is normally spelled with; a project that names its base differently overrides the parameter.
    private const string DefaultEntityBaseTypes = "Entity,EntityBase,AggregateRoot,AggregateRootBase,DomainEntity";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, EntityMessageFormat);
    private static readonly string[] MinimalApiMapMethods = ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete"];
    private static readonly HashSet<string> MvcResultMethods = new(StringComparer.Ordinal)
    {
        "Accepted",
        "AcceptedAtAction",
        "AcceptedAtRoute",
        "BadRequest",
        "Conflict",
        "Created",
        "CreatedAtAction",
        "CreatedAtRoute",
        "Json",
        "NotFound",
        "Ok",
        "StatusCode",
        "UnprocessableEntity",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("entityBaseTypes", PropertyType.String, "Comma-separated base types whose descendants are entities", DefaultEntityBaseTypes)]
    public string EntityBaseTypes { get; set; } = DefaultEntityBaseTypes;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var entities = GpEntityTypes.Create(start.Compilation, EntityBaseTypes);
            start.RegisterNodeAction(c => AnalyzeMethod(c, entities), SyntaxKind.MethodDeclaration);
            start.RegisterNodeAction(c => AnalyzeResultInvocation(c, entities), SyntaxKind.InvocationExpression);
        });

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context, GpEntityTypes entities)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(methodDeclaration) is not { } method
            || !method.IsControllerActionMethod
            || Unwrap(method.ReturnType) is not { } returned)
        {
            return;
        }

        if (IsQueryable(returned))
        {
            context.ReportIssue(Rule, methodDeclaration.ReturnType, returned.Name);
        }
        else if (ElementType(returned) is { } element && entities.IsEntity(element))
        {
            context.ReportIssue(Rule, methodDeclaration.ReturnType, element.Name);
        }
    }

    private static void AnalyzeResultInvocation(SonarSyntaxNodeReportingContext context, GpEntityTypes entities)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (TryGetMinimalApiPayload(context, invocation, out var value)
            || TryGetMvcPayload(context, invocation, out value))
        {
            ReportPayload(context, invocation, value, entities);
        }
    }

    private static bool TryGetMinimalApiPayload(SonarSyntaxNodeReportingContext context,
                                                InvocationExpressionSyntax invocation,
                                                out ExpressionSyntax value)
    {
        value = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        return value is not null
            && GpMinimalApi.TryGetResultMethod(context.Model, invocation, out var method)
            && method.Name is "Ok" or "Json"
            && GpMinimalApi.TryGetInlineHandler(invocation, context.Model, MinimalApiMapMethods, out _, out _, out _, out _);
    }

    private static bool TryGetMvcPayload(SonarSyntaxNodeReportingContext context,
                                         InvocationExpressionSyntax invocation,
                                         out ExpressionSyntax value)
    {
        value = null;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !MvcResultMethods.Contains(method.Name)
            || method.ContainingType?.ToDisplayString() is not ("Microsoft.AspNetCore.Mvc.ControllerBase" or "Microsoft.AspNetCore.Mvc.Controller")
            || context.Model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol enclosing
            || !enclosing.IsControllerActionMethod)
        {
            return false;
        }

        var lookup = new CSharpMethodParameterLookup(invocation, method);
        value = new[] { "value", "data", "error" }
            .SelectMany(x => lookup.TryGetSyntax(x, out var arguments)
                ? arguments.AsEnumerable()
                : Enumerable.Empty<ArgumentSyntax>())
            .OfType<ExpressionSyntax>()
            .FirstOrDefault();
        return value is not null;
    }

    private static void ReportPayload(SonarSyntaxNodeReportingContext context,
                                      InvocationExpressionSyntax invocation,
                                      ExpressionSyntax value,
                                      GpEntityTypes entities)
    {
        if (context.Model.GetTypeInfo(value).Type is { } valueType
            && PayloadProblem(valueType, entities) is { } problem)
        {
            context.ReportIssue(Rule, invocation, problem.Name);
        }
    }

    private static ITypeSymbol PayloadProblem(ITypeSymbol type, GpEntityTypes entities)
    {
        if (IsQueryable(type))
        {
            return type;
        }

        return ElementType(type) is { } element && entities.IsEntity(element)
            ? element
            : null;
    }

    // Task<T>, ValueTask<T> and ActionResult<T> only wrap what the endpoint really returns.
    private static ITypeSymbol Unwrap(ITypeSymbol type)
    {
        var current = type;
        while (current is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named && IsWrapper(named))
        {
            current = named.TypeArguments[0];
        }

        return current;
    }

    private static bool IsWrapper(INamedTypeSymbol type) =>
        type.ConstructedFrom.IsAny(KnownType.System_Threading_Tasks_Task_T, KnownType.System_Threading_Tasks_ValueTask_TResult)
        || (type.Name == "ActionResult" && type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc");

    // A collection of entities is just as much a leak as a single one.
    private static ITypeSymbol ElementType(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named when GpCollectionEndpointHelper.IsCollectionLike(named) => named.TypeArguments[0],
            _ => type,
        };

    private static bool IsQueryable(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.ToDisplayString() is "System.Linq.IQueryable<T>" or "System.Linq.IQueryable";
}
