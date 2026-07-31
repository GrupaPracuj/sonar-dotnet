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
    private static readonly Dictionary<string, string> JunoParameterlessClaimCheckMethods = new(StringComparer.Ordinal)
    {
        ["HasUserClaim"] = "sub",
        ["FindUserClaim"] = "sub",
        ["HasApplicationClaim"] = "app",
        ["FindApplicationClaim"] = "app",
        ["HasUserGroupClaim"] = "userGroup",
        ["FindUserGroupClaim"] = "userGroup",
        ["HasCompanyClaim"] = "company"
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
        if (context.Node is PrefixUnaryExpressionSyntax { Operand: InvocationExpressionSyntax invocation } && IsHasClaimInvocation(invocation))
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
            context.ReportIssue(IdentityClaimRule, invocation, junoClaimName);
            return;
        }

        if (methodName != "HasClaim")
        {
            return;
        }

        var claimName = ExtractClaimName(invocation.ArgumentList.Arguments, context.Model);
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

    private static bool IsHasClaimInvocation(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var methodName }
        && (methodName == "HasClaim" || (JunoParameterlessClaimCheckMethods.ContainsKey(methodName) && methodName.StartsWith("Has", StringComparison.Ordinal)));

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
            ParenthesizedLambdaExpressionSyntax lambda => ExtractClaimNameFromPredicate(lambda, model),
            SimpleLambdaExpressionSyntax lambda => ExtractClaimNameFromPredicate(lambda, model),
            AnonymousMethodExpressionSyntax anonymousMethod => ExtractClaimNameFromPredicate(anonymousMethod, model),
            _ => null
        };
    }

    private static string ExtractClaimNameFromPredicate(LambdaExpressionSyntax lambda, SemanticModel model)
    {
        var candidates = lambda.Body.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>();
        foreach (var binary in candidates)
        {
            if (binary.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
            {
                continue;
            }

            if (IsClaimTypeAccess(binary.Left))
            {
                var claimName = ExtractClaimNameFromExpression(binary.Right, model);
                if (claimName is not null)
                {
                    return claimName;
                }
            }

            if (IsClaimTypeAccess(binary.Right))
            {
                var claimName = ExtractClaimNameFromExpression(binary.Left, model);
                if (claimName is not null)
                {
                    return claimName;
                }
            }
        }

        return null;
    }

    private static string ExtractClaimNameFromPredicate(AnonymousMethodExpressionSyntax anonymousMethod, SemanticModel model)
    {
        var candidates = anonymousMethod.Body?.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>() ?? Enumerable.Empty<BinaryExpressionSyntax>();
        foreach (var binary in candidates)
        {
            if (binary.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
            {
                continue;
            }

            if (IsClaimTypeAccess(binary.Left))
            {
                var claimName = ExtractClaimNameFromExpression(binary.Right, model);
                if (claimName is not null)
                {
                    return claimName;
                }
            }

            if (IsClaimTypeAccess(binary.Right))
            {
                var claimName = ExtractClaimNameFromExpression(binary.Left, model);
                if (claimName is not null)
                {
                    return claimName;
                }
            }
        }

        return null;
    }

    private static bool IsClaimTypeAccess(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Type" };

    private static string ExtractClaimNameFromExpression(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        if (expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var memberName } memberAccess
            && memberAccess.Expression is IdentifierNameSyntax { Identifier.ValueText: "ClaimTypes" }
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
