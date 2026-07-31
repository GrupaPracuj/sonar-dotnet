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

    private static readonly DiagnosticDescriptor NegativeHasClaimRule = DescriptorFactory.Create(NegativeHasClaimRuleId, NegativeHasClaimMessage);
    private static readonly DiagnosticDescriptor IdentityClaimRule = DescriptorFactory.Create(IdentityClaimRuleId, IdentityClaimMessage);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(NegativeHasClaimRule, IdentityClaimRule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(CheckNegatedHasClaim, SyntaxKind.LogicalNotExpression);
        context.RegisterNodeAction(CheckHasClaimInvocation, SyntaxKind.InvocationExpression);
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
        if (context.Node is not InvocationExpressionSyntax invocation || !IsHasClaimInvocation(invocation))
        {
            return;
        }

        var claimName = ExtractClaimName(invocation.ArgumentList.Arguments);
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
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "HasClaim" };

    private static bool IsAuthorizeAttribute(AttributeSyntax attribute) =>
        attribute.Name switch
        {
            IdentifierNameSyntax { Identifier.ValueText: "Authorize" or "AuthorizeAttribute" } => true,
            QualifiedNameSyntax { Right.Identifier.ValueText: "Authorize" or "AuthorizeAttribute" } => true,
            _ => false
        };

    private static string ExtractClaimName(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (arguments.Count == 0)
        {
            return null;
        }

        return arguments[0].Expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: var memberName } memberAccess
                when memberAccess.Expression is IdentifierNameSyntax { Identifier.ValueText: "ClaimTypes" }
                     && ForbiddenClaimTypesMembers.Contains(memberName) => memberName,
            _ => null
        };
    }

    private static bool IsForbiddenIdentityClaim(string claimName) =>
        ForbiddenIdentityClaims.Contains(claimName) || ForbiddenClaimTypesMembers.Contains(claimName);
}
