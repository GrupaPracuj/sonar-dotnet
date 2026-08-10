namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClaimsAuthorizationShouldNotUseIdentityClaims : SonarDiagnosticAnalyzer
{
    internal const string NegativeHasClaimRuleId = "GP0005";
    internal const string IdentityClaimRuleId = "GP0006";

    private const string NegativeHasClaimMessage = "Do not grant access when the required claim is absent.";
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
        context.RegisterNodeAction(CheckClaimGuard, SyntaxKind.IfStatement);
        context.RegisterNodeAction(CheckHasClaimInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(CheckJunoClaimLookupInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(CheckAuthorizeAttribute, SyntaxKind.Attribute);
    }

    private static void CheckClaimGuard(SonarSyntaxNodeReportingContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;
        if (!TryGetClaimPresenceCheck(ifStatement.Condition, context.Model, out var claimCheck, out var claimPresentWhenTrue))
        {
            return;
        }

        var missingClaimBranch = claimPresentWhenTrue ? ifStatement.Else?.Statement : ifStatement.Statement;
        if (BranchDirectlyGrantsAccess(missingClaimBranch, context.Model))
        {
            context.ReportIssue(NegativeHasClaimRule, claimCheck);
        }
    }

    private static bool TryGetClaimPresenceCheck(ExpressionSyntax condition,
                                                 SemanticModel model,
                                                 out ExpressionSyntax claimCheck,
                                                 out bool claimPresentWhenTrue)
    {
        condition = RemoveParentheses(condition);
        if (condition is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression, Operand: var operand })
        {
            operand = RemoveParentheses(operand);
            if (operand is InvocationExpressionSyntax invocation && IsHasClaimInvocation(model, invocation))
            {
                claimCheck = condition;
                claimPresentWhenTrue = false;
                return true;
            }
        }
        else if (condition is InvocationExpressionSyntax invocation && IsHasClaimInvocation(model, invocation))
        {
            claimCheck = invocation;
            claimPresentWhenTrue = true;
            return true;
        }

        claimCheck = null;
        claimPresentWhenTrue = false;
        return false;
    }

    private static ExpressionSyntax RemoveParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }
        return expression;
    }

    private static bool BranchDirectlyGrantsAccess(StatementSyntax statement, SemanticModel model) =>
        DirectReturnExpressions(statement).Any(x => IsAccessGrant(x, model));

    private static IEnumerable<ExpressionSyntax> DirectReturnExpressions(StatementSyntax statement) =>
        statement switch
        {
            ReturnStatementSyntax { Expression: { } expression } => new[] { expression },
            BlockSyntax block => block.Statements.OfType<ReturnStatementSyntax>().Select(x => x.Expression).WhereNotNull(),
            _ => Enumerable.Empty<ExpressionSyntax>()
        };

    private static bool IsAccessGrant(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is not InvocationExpressionSyntax invocation
            || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        if (GpMinimalApi.TryGetResultMethod(model, invocation, out var resultMethod))
        {
            return IsSuccessfulResponse(resultMethod.Name, invocation.ArgumentList.Arguments, model);
        }

        return method.ContainingType?.ToDisplayString() is "Microsoft.AspNetCore.Mvc.ControllerBase" or "Microsoft.AspNetCore.Mvc.Controller"
               && IsSuccessfulResponse(method.Name, invocation.ArgumentList.Arguments, model);
    }

    private static bool IsSuccessfulResponse(string methodName, SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model)
    {
        if (methodName is "Ok" or "Created" or "CreatedAtAction" or "CreatedAtRoute" or "Accepted" or "AcceptedAtAction"
            or "AcceptedAtRoute" or "NoContent" or "Content" or "Json" or "File")
        {
            return true;
        }

        return methodName == "StatusCode"
               && arguments.FirstOrDefault()?.Expression is { } statusCodeExpression
               && model.GetConstantValue(statusCodeExpression) is { HasValue: true, Value: int statusCode }
               && statusCode is >= 200 and < 300;
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
                && IsJunoSecurityMethod(context.Model, invocation)
                && IsRecognizedAuthorizationDecision(invocation, context.Model))
            {
                context.ReportIssue(IdentityClaimRule, invocation, junoClaimName);
            }

            return;
        }

        if (methodName != "HasClaim"
            || !IsClaimsPrincipalMethod(context.Model, invocation)
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

        if (predicateBody is null
            || !IsRecognizedAuthorizationDecision(invocation, context.Model)
            || LambdaParameterName(firstArgument) is not { } parameterName
            || !ComparesClaimValue(predicateBody, parameterName))
        {
            return;
        }

        var claimName = ExtractClaimNameFromPredicate(predicateBody, context.Model, parameterName);
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

    private static bool ComparesClaimValue(SyntaxNode predicateBody, string parameterName) =>
        predicateBody.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>()
            .Any(x => x.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
                      && (IsClaimMemberAccess(x.Left, parameterName, "Value") || IsClaimMemberAccess(x.Right, parameterName, "Value")));

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
        && (methodName == "HasClaim" && IsClaimsPrincipalMethod(model, invocation)
            || (JunoParameterlessClaimCheckMethods.ContainsKey(methodName)
                && methodName.StartsWith("Has", StringComparison.Ordinal)
                && IsJunoSecurityMethod(model, invocation)));

    private static bool IsClaimsPrincipalMethod(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
        && GpJunoTypes.DerivesFrom(method.ContainingType, "System.Security.Claims.ClaimsPrincipal");

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
            ParenthesizedLambdaExpressionSyntax lambda => ExtractClaimNameFromPredicate(lambda.Body, model, lambda.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText),
            SimpleLambdaExpressionSyntax lambda => ExtractClaimNameFromPredicate(lambda.Body, model, lambda.Parameter.Identifier.ValueText),
            AnonymousMethodExpressionSyntax lambda => ExtractClaimNameFromPredicate(lambda.Body, model, lambda.ParameterList?.Parameters.FirstOrDefault()?.Identifier.ValueText),
            _ => null
        };
    }

    // Finds the claim type a predicate compares against, e.g. x => x.Type == ClaimTypes.Email.
    private static string ExtractClaimNameFromPredicate(SyntaxNode predicateBody, SemanticModel model, string parameterName)
    {
        if (parameterName is null)
        {
            return null;
        }

        foreach (var binary in predicateBody.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>())
        {
            if (binary.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
            {
                continue;
            }

            var comparedToClaimType = IsClaimMemberAccess(binary.Left, parameterName, "Type")
                ? binary.Right
                : IsClaimMemberAccess(binary.Right, parameterName, "Type") ? binary.Left : null;

            if (comparedToClaimType is not null && ExtractClaimNameFromExpression(comparedToClaimType, model) is { } claimName)
            {
                return claimName;
            }
        }

        return null;
    }

    private static bool IsClaimMemberAccess(ExpressionSyntax expression, string parameterName, string memberName) =>
        expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: var receiverName },
            Name.Identifier.ValueText: var accessedMember
        } && receiverName == parameterName && accessedMember == memberName;

    private static string LambdaParameterName(ExpressionSyntax expression) =>
        expression switch
        {
            ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText,
            SimpleLambdaExpressionSyntax lambda => lambda.Parameter.Identifier.ValueText,
            AnonymousMethodExpressionSyntax lambda => lambda.ParameterList?.Parameters.FirstOrDefault()?.Identifier.ValueText,
            _ => null
        };

    private static bool IsRecognizedAuthorizationDecision(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (IsInsideJunoAlternativePermissionPredicate(invocation, model))
        {
            return true;
        }

        var member = invocation.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        return member switch
        {
            MethodDeclarationSyntax method when model.GetDeclaredSymbol(method)?.ReturnType.SpecialType == SpecialType.System_Boolean =>
                GpIdentifierWords.ContainsWord(method.Identifier.ValueText, "Access")
                || GpIdentifierWords.ContainsWord(method.Identifier.ValueText, "Authorize")
                || GpIdentifierWords.ContainsWord(method.Identifier.ValueText, "Authorization")
                || GpIdentifierWords.ContainsWord(method.Identifier.ValueText, "Permission"),
            PropertyDeclarationSyntax property when model.GetDeclaredSymbol(property)?.Type.SpecialType == SpecialType.System_Boolean =>
                GpIdentifierWords.ContainsWord(property.Identifier.ValueText, "Access")
                || GpIdentifierWords.ContainsWord(property.Identifier.ValueText, "Authorized")
                || GpIdentifierWords.ContainsWord(property.Identifier.ValueText, "Permission"),
            _ => false
        };
    }

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
