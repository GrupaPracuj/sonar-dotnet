/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishedMessagesShouldComeFromContractAssemblies : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0043";

    private const string MessageFormat = "Use '{0}' from a contract assembly for this {1}; it is declared in '{2}'.";
    private const string DefaultContractAssemblyNames = "Contracts";
    private const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string FromBodyAttribute = "Microsoft.AspNetCore.Mvc.FromBodyAttribute";
    private const string SwaggerIgnoreAttribute = "Swashbuckle.AspNetCore.Annotations.SwaggerIgnoreAttribute";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> MessagingMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Publishes",
        "RespondAsync",
        "Send",
        "Sends",
    };
    private static readonly HashSet<string> ResponseFactoryMethods = new(StringComparer.Ordinal)
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
    private static readonly HashSet<string> ServiceBindingAttributes = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Mvc.FromServicesAttribute",
        "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute",
    };
    private static readonly string[] MinimalApiMapMethods = ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("contractAssemblyNames", PropertyType.String, "Comma-separated names or suffixes identifying contract assemblies", DefaultContractAssemblyNames)]
    public string ContractAssemblyNames { get; set; } = DefaultContractAssemblyNames;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var contractAssemblyNames = GpEntityTypes.SplitParameter(ContractAssemblyNames);
            if (contractAssemblyNames.Length > 0)
            {
                start.RegisterNodeAction(c => AnalyzeInvocation(c, contractAssemblyNames), SyntaxKind.InvocationExpression);
                start.RegisterNodeAction(c => AnalyzeControllerAction(c, contractAssemblyNames), SyntaxKind.MethodDeclaration);
                start.RegisterNodeAction(
                    c => AnalyzeMinimalApiHandler(c, contractAssemblyNames),
                    SyntaxKind.SimpleLambdaExpression,
                    SyntaxKind.ParenthesizedLambdaExpression,
                    SyntaxKind.AnonymousMethodExpression);
            }
        });

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context, string[] contractAssemblyNames)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (MessagingMethods.Contains(method.Name)
            && GpMessageContracts.MessagingPayloadType(context.Model, invocation, MessagingMethods) is { } messageType
            && GpMessageContracts.DescribeShapelessType(messageType) is null)
        {
            ReportIfOutsideContracts(
                context,
                invocation,
                messageType,
                method.Name is "Send" or "Sends" ? "sent command" : "published message",
                contractAssemblyNames);
            return;
        }

        if (ResponseFactoryMethods.Contains(method.Name)
            && IsRestResponseFactory(context, invocation)
            && ResponsePayload(context.Model, invocation, method) is { } responseType)
        {
            ReportIfOutsideContracts(context, invocation, responseType, "REST response", contractAssemblyNames);
        }
    }

    private static void AnalyzeControllerAction(SonarSyntaxNodeReportingContext context, string[] contractAssemblyNames)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is not { IsControllerActionMethod: true } method)
        {
            return;
        }

        ReportIfOutsideContracts(context, declaration.ReturnType, method.ReturnType, "REST response", contractAssemblyNames);
        foreach (var parameter in declaration.ParameterList.Parameters)
        {
            if (parameter.Type is not null
                && context.Model.GetDeclaredSymbol(parameter) is { } symbol
                && IsRestRequestParameter(symbol, method))
            {
                ReportIfOutsideContracts(context, parameter.Type, symbol.Type, "REST request", contractAssemblyNames);
            }
        }
    }

    private static void AnalyzeMinimalApiHandler(SonarSyntaxNodeReportingContext context, string[] contractAssemblyNames)
    {
        var handler = (AnonymousFunctionExpressionSyntax)context.Node;
        if (!GpMinimalApi.TryGetInlineHandler(
                handler.Body,
                context.Model,
                MinimalApiMapMethods,
                out var foundHandler,
                out _,
                out _,
                out _)
            || foundHandler != handler)
        {
            return;
        }

        foreach (var parameter in HandlerParameters(handler))
        {
            if (context.Model.GetDeclaredSymbol(parameter) is { } symbol
                && !IsServiceParameter(symbol)
                && !IsPotentialMinimalApiService(symbol.Type))
            {
                ReportIfOutsideContracts(
                    context,
                    parameter.Type is { } parameterType ? parameterType : parameter,
                    symbol.Type,
                    "REST request",
                    contractAssemblyNames);
            }
        }

        foreach (var expression in ReturnedExpressions(handler))
        {
            if (expression is InvocationExpressionSyntax invocation
                && GpMinimalApi.TryGetResultMethod(context.Model, invocation, out _))
            {
                continue;
            }

            ReportIfOutsideContracts(
                context,
                expression,
                context.Model.GetTypeInfo(expression).Type,
                "REST response",
                contractAssemblyNames);
        }
    }

    private static void ReportIfOutsideContracts(SonarSyntaxNodeReportingContext context,
                                                 SyntaxNode location,
                                                 ITypeSymbol payloadType,
                                                 string boundary,
                                                 string[] contractAssemblyNames)
    {
        if (ContractType(payloadType) is not { } contractType
            || contractType.ContainingAssembly?.Name is not { } assemblyName
            || contractAssemblyNames.Any(x => GpAssemblyNames.Matches(assemblyName, x)))
        {
            return;
        }

        context.ReportIssue(Rule, location, contractType.Name, boundary, assemblyName);
    }

    private static INamedTypeSymbol ContractType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return ContractType(array.ElementType);
        }

        if (type is not INamedTypeSymbol named
            || named.SpecialType != SpecialType.None
            || named.TypeKind == TypeKind.Error
            || named.IsAnonymousType)
        {
            return null;
        }

        if (!IsFrameworkType(named))
        {
            return named;
        }

        return named.TypeArguments.Select(ContractType).FirstOrDefault(x => x is not null);
    }

    private static bool IsFrameworkType(ITypeSymbol type) =>
        (type.ContainingNamespace?.ToDisplayString() ?? string.Empty) is var containing
        && (containing == "System"
            || containing.StartsWith("System.", StringComparison.Ordinal)
            || containing == "Microsoft"
            || containing.StartsWith("Microsoft.", StringComparison.Ordinal));

    private static bool IsServiceParameter(IParameterSymbol parameter) =>
        parameter.GetAttributes().Any(x => ServiceBindingAttributes.Contains(x.AttributeClass?.ToDisplayString() ?? string.Empty));

    private static bool IsRestRequestParameter(IParameterSymbol parameter, IMethodSymbol action) =>
        !IsServiceParameter(parameter)
        && !HasAttribute(parameter, SwaggerIgnoreAttribute)
        && (IsApiController(action) || HasAttribute(parameter, FromBodyAttribute));

    private static bool IsApiController(IMethodSymbol action)
    {
        if (action.ContainingAssembly.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == ApiControllerAttribute))
        {
            return true;
        }

        for (var type = action.ContainingType; type is not null; type = type.BaseType)
        {
            if (HasAttribute(type, ApiControllerAttribute))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, string attributeType) =>
        symbol.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == attributeType);

    // FN: Minimal API can resolve an unannotated interface or abstract parameter from DI at runtime. Without the
    // service registrations there is no semantic evidence that it is an HTTP payload, so reporting it would be noisy.
    private static bool IsPotentialMinimalApiService(ITypeSymbol type) =>
        type is { TypeKind: TypeKind.Interface } or { IsAbstract: true };

    private static bool IsRestResponseFactory(SonarSyntaxNodeReportingContext context, InvocationExpressionSyntax invocation)
    {
        if (GpMvcResults.TryGetResultMethod(context.Model, invocation, out _))
        {
            return context.Model.GetEnclosingSymbol(invocation.SpanStart) is IMethodSymbol { IsControllerActionMethod: true } enclosing
                && ContractType(enclosing.ReturnType) is null;
        }

        return GpMinimalApi.TryGetResultMethod(context.Model, invocation, out _)
            && GpMinimalApi.TryGetInlineHandler(invocation, context.Model, MinimalApiMapMethods, out _, out _, out _, out _);
    }

    private static ITypeSymbol ResponsePayload(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        var lookup = new CSharpMethodParameterLookup(invocation, method);
        var payload = new[] { "value", "data", "error" }
            .SelectMany(x => lookup.TryGetSyntax(x, out var arguments)
                ? arguments.AsEnumerable()
                : Enumerable.Empty<SyntaxNode>())
            .OfType<ExpressionSyntax>()
            .FirstOrDefault();
        return payload is null ? null : model.GetTypeInfo(payload).Type;
    }

    private static IEnumerable<ParameterSyntax> HandlerParameters(AnonymousFunctionExpressionSyntax handler) =>
        handler switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters,
            AnonymousMethodExpressionSyntax { ParameterList: { } parameters } => parameters.Parameters,
            _ => Enumerable.Empty<ParameterSyntax>(),
        };

    private static IEnumerable<ExpressionSyntax> ReturnedExpressions(AnonymousFunctionExpressionSyntax handler)
    {
        if (handler.Body is ExpressionSyntax expression)
        {
            yield return expression;
            yield break;
        }

        foreach (var returned in handler.Body.DescendantNodes(x =>
                     x.Kind() is not (SyntaxKindEx.LocalFunctionStatement
                         or SyntaxKind.SimpleLambdaExpression
                         or SyntaxKind.ParenthesizedLambdaExpression
                         or SyntaxKind.AnonymousMethodExpression))
                 .OfType<ReturnStatementSyntax>()
                 .Select(x => x.Expression)
                 .WhereNotNull())
        {
            yield return returned;
        }
    }
}
