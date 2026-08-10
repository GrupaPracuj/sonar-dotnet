namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClaimsAuthorizationShouldNotUseIdentityClaims : SonarDiagnosticAnalyzer
{
    internal const string NegativeHasClaimRuleId = "GP0005";
    internal const string IdentityClaimRuleId = "GP0006";

    private const string NegativeHasClaimMessage = "Do not base access decisions on a negated HasClaim check.";
    private const string IdentityClaimMessage = "Do not base access control on identity claim '{0}'.";

    private static readonly HashSet<string> ForbiddenIdentityClaims = new(StringComparer.OrdinalIgnoreCase)
    {
        "sub",
        "name",
        "email",
        "phone",
        "phone_number",
        "preferred_username",
        "upn",
        "given_name",
        "family_name",
        "unique_name"
    };

    private static readonly HashSet<string> ForbiddenClaimTypesMembers = new(StringComparer.Ordinal)
    {
        "NameIdentifier",
        "Name",
        "Email",
        "MobilePhone",
        "Upn",
        "GivenName",
        "Surname"
    };

    private static readonly HashSet<string> JunoAlternativePermissionMethods = new(StringComparer.Ordinal)
    {
        "OrCalledByApi",
        "AddUserActivitiesAlternative"
    };

    // GP.Juno.Security(.UserContexts) exposes its own parameterless claim-existence checks, each tied to one fixed
    // claim type (see GP.Juno CustomClaimTypes / ClaimPrincipalExtensions.ClaimTypeUserId): these bypass the generic
    // HasClaim(string)/HasClaim(predicate) overloads entirely, so they need their own name-to-claim mapping.
    //
    // HasCompanyClaim/FindCompanyClaim are deliberately not listed here: unlike the identity claims above, "company"
    // is an accepted authorization dimension in this organization (e.g. gating access to company-scoped resources
    // on a multi-tenant platform), not an identity leak, so it is not flagged by this rule.
    private static readonly Dictionary<string, string> JunoParameterlessClaimCheckMethods = new(StringComparer.Ordinal)
    {
        ["HasUserClaim"] = "sub",
        ["FindUserClaim"] = "sub",
        ["HasApplicationClaim"] = "app",
        ["FindApplicationClaim"] = "app",
        ["HasUserGroupClaim"] = "userGroup",
        ["FindUserGroupClaim"] = "userGroup"
    };

    private static readonly DiagnosticDescriptor NegativeHasClaimRule = DescriptorFactory.Create(NegativeHasClaimRuleId, NegativeHasClaimMessage);
    private static readonly DiagnosticDescriptor IdentityClaimRule = DescriptorFactory.Create(IdentityClaimRuleId, IdentityClaimMessage);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(NegativeHasClaimRule, IdentityClaimRule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(CheckNegatedHasClaim, SyntaxKind.LogicalNotExpression);
        context.RegisterNodeAction(CheckHasClaimInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(CheckJunoClaimLookupInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(CheckAuthorizeAttribute, SyntaxKind.Attribute);
    }

    private static void CheckNegatedHasClaim(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is PrefixUnaryExpressionSyntax { Operand: InvocationExpressionSyntax invocation } && IsHasClaimInvocation(context.Model, invocation))
        {
            context.ReportIssue(NegativeHasClaimRule, context.Node);
        }
    }

    private static void CheckHasClaimInvocation(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var methodName })
        {
            return;
        }

        if (JunoParameterlessClaimCheckMethods.TryGetValue(methodName, out var junoClaimName))
        {
            // Has*Claim() returns a bool - it can only ever check whether the claim is present, never what its
            // value is, so it is out of scope for this rule entirely. Find*Claim() returns the claim itself: only
            // flag it when the caller actually reads its Value, not when it merely checks whether the claim was
            // found (a null/HasValue check) - see ResultValueIsAccessed. The name alone is not enough either way:
            // only the GP.Juno claim helpers imply a fixed claim type, so an unrelated method that happens to share
            // the name must not be reported.
            if (methodName.StartsWith("Find", StringComparison.Ordinal)
                && ResultValueIsAccessed(invocation)
                && IsJunoSecurityMethod(context.Model, invocation))
            {
                context.ReportIssue(IdentityClaimRule, invocation, junoClaimName);
            }

            return;
        }

        if (methodName != "HasClaim"
            || invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } firstArgument)
        {
            return;
        }

        // HasClaim(string) is an existence check by construction - there is no value to compare, so it is never
        // flagged, regardless of claim name. Only the predicate overload can express a value comparison, and only
        // when it actually does (e.g. 'c => c.Type == "sub" && c.Value == someId'), not when it only matches Type.
        var predicateBody = firstArgument switch
        {
            ParenthesizedLambdaExpressionSyntax { Body: { } body } => body,
            SimpleLambdaExpressionSyntax { Body: { } body } => body,
            AnonymousMethodExpressionSyntax { Body: { } body } => body,
            _ => null
        };

        if (predicateBody is null || !ComparesClaimValue(predicateBody))
        {
            return;
        }

        var claimName = ExtractClaimNameFromPredicate(predicateBody, context.Model);
        if (claimName is not null && IsForbiddenIdentityClaim(claimName))
        {
            context.ReportIssue(IdentityClaimRule, invocation, claimName);
        }
    }

    private static void CheckJunoClaimLookupInvocation(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var methodName }
            || methodName is not ("FindFirst" or "FindAll")
            || !ResultValueIsAccessed(invocation)
            || !IsInsideJunoAlternativePermissionPredicate(invocation, context.Model))
        {
            return;
        }

        var claimName = ExtractClaimName(invocation.ArgumentList.Arguments, context.Model);
        if (claimName is not null && IsForbiddenIdentityClaim(claimName))
        {
            context.ReportIssue(IdentityClaimRule, invocation, claimName);
        }
    }

    // Existence-only checks - is the claim present, absent, or of a given type - are not what this rule targets:
    // only a comparison against the claim's actual Value ties an access decision to a specific identity, rather
    // than merely to whether some claim of that type showed up in the token at all.
    private static bool IsValueAccess(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" };

    private static bool ComparesClaimValue(SyntaxNode predicateBody) =>
        predicateBody.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>()
            .Any(x => x.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression && (IsValueAccess(x.Left) || IsValueAccess(x.Right)));

    // True when the caller reads .Value (or ?.Value) off the invocation's result, rather than merely checking
    // whether it is null/HasValue - e.g. 'FindFirst(...).Value == x' is a value comparison, 'FindFirst(...) != null'
    // is not.
    private static bool ResultValueIsAccessed(InvocationExpressionSyntax invocation) =>
        invocation.Parent switch
        {
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" } => true,
            ConditionalAccessExpressionSyntax { WhenNotNull: MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Value" } } => true,
            _ => false
        };

    private static void CheckAuthorizeAttribute(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not AttributeSyntax attribute || !IsAuthorizeAttribute(attribute))
        {
            return;
        }

        var policyArgument = attribute.ArgumentList?.Arguments.FirstOrDefault(x =>
            x.NameEquals?.Name is IdentifierNameSyntax { Identifier.ValueText: "Policy" });

        if (policyArgument?.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var policyName = literal.Token.ValueText;
            if (IsForbiddenIdentityClaim(policyName))
            {
                context.ReportIssue(IdentityClaimRule, literal, policyName);
            }
        }
    }

    private static bool IsHasClaimInvocation(SemanticModel model, InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var methodName }
        && (methodName == "HasClaim"
            || (JunoParameterlessClaimCheckMethods.ContainsKey(methodName)
                && methodName.StartsWith("Has", StringComparison.Ordinal)
                && IsJunoSecurityMethod(model, invocation)));

    private static bool IsJunoSecurityMethod(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return IsJunoNamespace(method.ContainingNamespace) || IsJunoNamespace(method.ContainingType?.ContainingNamespace);
    }

    private static bool IsJunoNamespace(INamespaceSymbol namespaceSymbol) =>
        (namespaceSymbol?.ToDisplayString() ?? string.Empty).StartsWith("GP.Juno", StringComparison.Ordinal);

    private static bool IsAuthorizeAttribute(AttributeSyntax attribute) =>
        attribute.Name switch
        {
            IdentifierNameSyntax { Identifier.ValueText: "Authorize" or "AuthorizeAttribute" } => true,
            QualifiedNameSyntax { Right.Identifier.ValueText: "Authorize" or "AuthorizeAttribute" } => true,
            _ => false
        };

    private static bool IsInsideJunoAlternativePermissionPredicate(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        foreach (var ancestorInvocation in invocation.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            if (!IsJunoAlternativePermissionInvocation(ancestorInvocation, model))
            {
                continue;
            }

            var hasContainingLambda = ancestorInvocation.ArgumentList.Arguments
                .Select(x => x.Expression)
                .Any(lambda => lambda is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax
                               && lambda.Span.Contains(invocation.Span));

            if (hasContainingLambda)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJunoAlternativePermissionInvocation(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !JunoAlternativePermissionMethods.Contains(method.Name))
        {
            return false;
        }

        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return namespaceName.IndexOf("GP.Juno.Hosting.AspNetCore.Security.UserActivities.DependencyInjection", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ExtractClaimName(SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model)
    {
        if (arguments.Count == 0)
        {
            return null;
        }

        var firstArgument = arguments[0].Expression;

        var directClaimName = ExtractClaimNameFromExpression(firstArgument, model);
        if (directClaimName is not null)
        {
            return directClaimName;
        }

        return firstArgument switch
        {
            ParenthesizedLambdaExpressionSyntax { Body: { } body } => ExtractClaimNameFromPredicate(body, model),
            SimpleLambdaExpressionSyntax { Body: { } body } => ExtractClaimNameFromPredicate(body, model),
            AnonymousMethodExpressionSyntax { Body: { } body } => ExtractClaimNameFromPredicate(body, model),
            _ => null
        };
    }

    // Finds the claim type a predicate compares against, e.g. x => x.Type == ClaimTypes.Email.
    private static string ExtractClaimNameFromPredicate(SyntaxNode predicateBody, SemanticModel model)
    {
        foreach (var binary in predicateBody.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>())
        {
            if (binary.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
            {
                continue;
            }

            var comparedToClaimType = IsClaimTypeAccess(binary.Left)
                ? binary.Right
                : IsClaimTypeAccess(binary.Right) ? binary.Left : null;

            if (comparedToClaimType is not null && ExtractClaimNameFromExpression(comparedToClaimType, model) is { } claimName)
            {
                return claimName;
            }
        }

        return null;
    }

    private static bool IsClaimTypeAccess(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Type" };

    // Accepts both "ClaimTypes.Email" and a qualified "System.Security.Claims.ClaimTypes.Email".
    private static bool IsClaimTypesQualifier(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax { Identifier.ValueText: "ClaimTypes" } => true,
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ClaimTypes" } => true,
            _ => false
        };

    private static string ExtractClaimNameFromExpression(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        if (expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var memberName } memberAccess
            && IsClaimTypesQualifier(memberAccess.Expression)
            && ForbiddenClaimTypesMembers.Contains(memberName))
        {
            return memberName;
        }

        return model.GetConstantValue(expression) is { HasValue: true, Value: string constantValue }
            ? constantValue
            : null;
    }

    private static bool IsForbiddenIdentityClaim(string claimName) =>
        ForbiddenIdentityClaims.Contains(claimName) || ForbiddenClaimTypesMembers.Contains(claimName);
}
