/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotSendEmailWithSmtpClient : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0037";

    private const string MessageFormat = "Send email through Juno's email sender instead of '{0}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> SmtpTypes = new(StringComparer.Ordinal)
    {
        "System.Net.Mail.SmtpClient",
        "System.Web.Mail.SmtpMail",
    };

    private static readonly HashSet<string> SendMethods = new(StringComparer.Ordinal)
    {
        "Send",
        "SendAsync",
        "SendMailAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(
            c =>
            {
                AnalyzeObjectCreation(c);
                AnalyzeInvocation(c);
            },
            SyntaxKind.ObjectCreationExpression,
            SyntaxKindEx.ImplicitObjectCreationExpression,
            SyntaxKind.InvocationExpression);

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && SmtpTypes.Contains(type.ToDisplayString()))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !SendMethods.Contains(method.Name)
            || !SmtpTypes.Contains(method.ContainingType?.ToDisplayString() ?? string.Empty)
            || invocation.Expression is MemberAccessExpressionSyntax memberAccess
               && IsAlreadyReportedConstruction(memberAccess.Expression, context.Model))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.ContainingType.Name);
    }

    private static bool IsAlreadyReportedConstruction(ExpressionSyntax receiver, SemanticModel model)
    {
        receiver = (ExpressionSyntax)receiver.RemoveParentheses();
        if (receiver is ObjectCreationExpressionSyntax)
        {
            return true;
        }

        return model.GetSymbolInfo(receiver).Symbol is ILocalSymbol or IFieldSymbol
               && model.GetSymbolInfo(receiver).Symbol.DeclaringSyntaxReferences
                   .Select(x => x.GetSyntax())
                   .OfType<VariableDeclaratorSyntax>()
                   .Any(x => x.Initializer?.Value.RemoveParentheses() is ObjectCreationExpressionSyntax creation
                             && SmtpTypes.Contains(model.GetTypeInfo(creation).Type?.ToDisplayString() ?? string.Empty));
    }
}
