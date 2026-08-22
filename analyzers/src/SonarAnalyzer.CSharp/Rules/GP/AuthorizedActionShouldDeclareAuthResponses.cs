/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuthorizedActionShouldDeclareAuthResponses : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0115";

    private const string MessageFormat = "Declare the {0} this authorized action can return.";

    private const string AllowAnonymousAttribute = "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";
    private const string AuthorizeAttribute = "Microsoft.AspNetCore.Authorization.AuthorizeAttribute";

    private const int Unauthorized = 401;
    private const int Forbidden = 403;

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
            || EffectiveAuthorization(method) is not { } authorize)
        {
            return;
        }

        var documented = GpOpenApiMetadata.ResponseAttributes(method)
            .Select(GpOpenApiMetadata.ResponseStatusCode)
            .WhereNotNull()
            .ToHashSet();

        var missing = RequiredStatusCodes(authorize).Where(x => !documented.Contains(x)).ToArray();
        if (missing.Length > 0)
        {
            context.ReportIssue(Rule, declaration.Identifier, Describe(missing));
        }
    }

    // [AllowAnonymous] anywhere in the chain that applies to the action wins over every [Authorize] - the framework
    // short-circuits authorization entirely - so such an action can never answer 401 or 403 for these attributes.
    // A bare [Authorize] only rejects unauthenticated callers (401); an authenticated one always passes it. Naming a
    // policy or a role adds a requirement an authenticated caller can fail, which is what produces 403.
    private static AttributeData EffectiveAuthorization(IMethodSymbol method)
    {
        if (HasAttribute(method, AllowAnonymousAttribute) || HasAttribute(method.ContainingType, AllowAnonymousAttribute))
        {
            return null;
        }

        return Attributes(method, AuthorizeAttribute).Concat(Attributes(method.ContainingType, AuthorizeAttribute))
            .OrderByDescending(RestrictsBeyondAuthentication)
            .FirstOrDefault();
    }

    private static IEnumerable<int> RequiredStatusCodes(AttributeData authorize)
    {
        yield return Unauthorized;
        if (RestrictsBeyondAuthentication(authorize))
        {
            yield return Forbidden;
        }
    }

    private static bool RestrictsBeyondAuthentication(AttributeData authorize) =>
        authorize.ConstructorArguments.Any(IsNonEmptyString)
        || authorize.NamedArguments.Any(x => x.Key is "Policy" or "Roles" && IsNonEmptyString(x.Value));

    private static bool IsNonEmptyString(TypedConstant argument) =>
        argument.Value is string { Length: > 0 };

    private static string Describe(int[] missing) =>
        missing.Length == 1
            ? $"{missing[0]} response"
            : $"{string.Join(" and ", missing)} responses";

    private static IEnumerable<AttributeData> Attributes(ISymbol symbol, string metadataName) =>
        symbol.AttributesWithInherited.Where(x => DerivesFrom(x.AttributeClass, metadataName));

    private static bool HasAttribute(ISymbol symbol, string metadataName) =>
        Attributes(symbol, metadataName).Any();

    private static bool DerivesFrom(INamedTypeSymbol type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == metadataName)
            {
                return true;
            }
        }
        return false;
    }
}
