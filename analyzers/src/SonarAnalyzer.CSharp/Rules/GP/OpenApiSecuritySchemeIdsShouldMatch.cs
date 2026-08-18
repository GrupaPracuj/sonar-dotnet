/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using System.Collections.Concurrent;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OpenApiSecuritySchemeIdsShouldMatch : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0108";

    private const string MessageFormat = "Security scheme reference '{0}' differs in casing from definition '{1}'.";
    private const string SwaggerGenOptionsType = "Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions";
    private const string SwaggerRegistrationType = "Microsoft.Extensions.DependencyInjection.SwaggerGenServiceCollectionExtensions";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var scopes = new ConcurrentDictionary<ScopeKey, SecuritySchemeScope>();
            start.RegisterNodeAction(c => CollectDefinition(c, scopes), SyntaxKind.InvocationExpression);
            start.RegisterNodeAction(
                c => CollectReference(c, scopes),
                SyntaxKind.ObjectCreationExpression,
                SyntaxKindEx.ImplicitObjectCreationExpression);
            start.RegisterCompilationEndAction(c => Report(c, scopes.Values));
        });

    private static void CollectDefinition(SonarSyntaxNodeReportingContext context,
                                          ConcurrentDictionary<ScopeKey, SecuritySchemeScope> scopes)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "AddSecurityDefinition" } method
            || !IsSwaggerGenOptionsMethod(method)
            || ArgumentExpression(invocation.ArgumentList.Arguments, method, 0) is not { } idExpression
            || ConstantString(context.Model, idExpression) is not { } id
            || SwaggerScope(context.Model, invocation) is not { } scope)
        {
            return;
        }

        scopes.GetOrAdd(scope, _ => new SecuritySchemeScope())
            .Definitions
            .Add(new IdUse(id, idExpression.GetLocation()));
    }

    private static void CollectReference(SonarSyntaxNodeReportingContext context,
                                         ConcurrentDictionary<ScopeKey, SecuritySchemeScope> scopes)
    {
        if (!ObjectCreationFactory.TryCreate(context.Node, out var creation)
            || creation.TypeSymbol(context.Model) is not { } type
            || ReferenceId(context.Model, creation, type) is not { } reference
            || SwaggerScope(context.Model, creation.Expression) is not { } scope)
        {
            return;
        }

        scopes.GetOrAdd(scope, _ => new SecuritySchemeScope())
            .References
            .Add(reference);
    }

    private static IdUse ReferenceId(SemanticModel model, IObjectCreation creation, ITypeSymbol type)
    {
        if (type.Name == "OpenApiSecuritySchemeReference"
            && IsMicrosoftOpenApiType(type)
            && model.GetSymbolInfo(creation.Expression).Symbol is IMethodSymbol constructor
            && ArgumentExpression(creation.ArgumentList.Arguments, constructor, 0) is { } idExpression
            && ConstantString(model, idExpression) is { } id)
        {
            return new IdUse(id, idExpression.GetLocation());
        }

        if (type.Name != "OpenApiReference" || !IsMicrosoftOpenApiType(type) || creation.Initializer is null)
        {
            return null;
        }

        ExpressionSyntax idValue = null;
        var isSecurityScheme = false;
        foreach (var assignment in creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>())
        {
            if (model.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol property)
            {
                continue;
            }

            if (property.Name == "Id")
            {
                idValue = assignment.Right;
            }
            else if (property.Name == "Type"
                     && model.GetSymbolInfo(assignment.Right).Symbol is IFieldSymbol
                     {
                         Name: "SecurityScheme",
                         ContainingType.Name: "ReferenceType",
                     })
            {
                isSecurityScheme = true;
            }
        }

        return isSecurityScheme && idValue is not null && ConstantString(model, idValue) is { } legacyId
            ? new IdUse(legacyId, idValue.GetLocation())
            : null;
    }

    private static void Report(SonarCompilationReportingContext context, IEnumerable<SecuritySchemeScope> scopes)
    {
        foreach (var scope in scopes)
        {
            var definitions = scope.Definitions.ToArray();
            foreach (var reference in scope.References)
            {
                if (definitions.Any(x => x.Id == reference.Id))
                {
                    continue;
                }

                var mismatch = definitions
                    .Where(x => string.Equals(x.Id, reference.Id, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (mismatch is not null)
                {
                    context.ReportIssue(
                        CSharpGeneratedCodeRecognizer.Instance,
                        Rule,
                        reference.Location,
                        messageArgs: new[] { reference.Id, mismatch.Id });
                }
            }
        }
    }

    private static ScopeKey? SwaggerScope(SemanticModel model, SyntaxNode node) =>
        node.Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Where(x => IsAddSwaggerGenLambda(model, x))
            .Select(x => (ScopeKey?)new ScopeKey(x.SyntaxTree, x.SpanStart))
            .FirstOrDefault();

    private static bool IsAddSwaggerGenLambda(SemanticModel model, AnonymousFunctionExpressionSyntax lambda) =>
        lambda.Parent is ArgumentSyntax
        {
            Parent: ArgumentListSyntax
            {
                Parent: InvocationExpressionSyntax invocation,
            },
        }
        && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol
        {
            Name: "AddSwaggerGen",
            ContainingType: { } containingType,
        }
        && containingType.ToDisplayString() == SwaggerRegistrationType;

    private static bool IsSwaggerGenOptionsMethod(IMethodSymbol method) =>
        method.ContainingType?.ToDisplayString() == SwaggerGenOptionsType
        || method.ReceiverType?.ToDisplayString() == SwaggerGenOptionsType;

    private static bool IsMicrosoftOpenApiType(ITypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() is { } ns
        && (ns == "Microsoft.OpenApi" || ns.StartsWith("Microsoft.OpenApi.", StringComparison.Ordinal));

    private static string ConstantString(SemanticModel model, ExpressionSyntax expression) =>
        model.GetConstantValue(expression) is { HasValue: true, Value: string value } ? value : null;

    private static ExpressionSyntax ArgumentExpression(SeparatedSyntaxList<ArgumentSyntax> arguments,
                                                       IMethodSymbol method,
                                                       int parameterOrdinal)
    {
        var parameterName = method.Parameters.FirstOrDefault(x => x.Ordinal == parameterOrdinal)?.Name;
        var namedArgument = arguments.FirstOrDefault(x => x.NameColon?.Name.Identifier.ValueText == parameterName);
        return namedArgument?.Expression
            ?? (arguments.Count > parameterOrdinal && arguments[parameterOrdinal].NameColon is null
                ? arguments[parameterOrdinal].Expression
                : null);
    }

    private readonly record struct ScopeKey(SyntaxTree Tree, int Start);

    private sealed class SecuritySchemeScope
    {
        public ConcurrentBag<IdUse> Definitions { get; } = new();
        public ConcurrentBag<IdUse> References { get; } = new();
    }

    private sealed record IdUse(string Id, Location Location);
}
