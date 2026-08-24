/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotSendEmailWithSmtpClient : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0037";

    private const string MessageFormat = "Use the approved email delivery abstraction instead of the obsolete '{0}'.";
    private const string DefaultAllowedAssemblyNames = "GP.Postman.Sender";
    private const string SmtpDeliveryMethodType = "System.Net.Mail.SmtpDeliveryMethod";

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

    [RuleParameter("allowedAssemblyNames", PropertyType.String, "Comma-separated assemblies that implement the approved SMTP delivery adapter", DefaultAllowedAssemblyNames)]
    public string AllowedAssemblyNames { get; set; } = DefaultAllowedAssemblyNames;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !SendMethods.Contains(method.Name)
            || !SmtpTypes.Contains(method.ContainingType?.ToDisplayString() ?? string.Empty)
            || IsAllowedAssembly(context.Model.Compilation.AssemblyName)
            || invocation.Expression is MemberAccessExpressionSyntax memberAccess
               && IsPickupDirectoryClient(memberAccess.Expression, invocation, context.Model))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.ContainingType.Name);
    }

    private bool IsAllowedAssembly(string assemblyName) =>
        GpEntityTypes.SplitParameter(AllowedAssemblyNames).Contains(assemblyName, StringComparer.OrdinalIgnoreCase);

    private static bool IsPickupDirectoryClient(ExpressionSyntax receiver, InvocationExpressionSyntax send, SemanticModel model)
    {
        receiver = (ExpressionSyntax)receiver.RemoveParentheses();
        if (receiver is ObjectCreationExpressionSyntax creation)
        {
            return InitializerUsesPickupDirectory(creation.Initializer, model);
        }

        return model.GetSymbolInfo(receiver).Symbol switch
        {
            ILocalSymbol local => LocalIsPickupDirectoryClient(local, send, model),
            IFieldSymbol { DeclaredAccessibility: Accessibility.Private, IsReadOnly: true } field =>
                FieldIsPickupDirectoryClient(field, model),
            _ => false,
        };
    }

    private static bool LocalIsPickupDirectoryClient(ILocalSymbol local, InvocationExpressionSyntax send, SemanticModel model)
    {
        var declaration = local.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<VariableDeclaratorSyntax>().SingleOrDefault();
        var sendStatement = send.FirstAncestorOrSelf<StatementSyntax>();
        if (declaration is null
            || sendStatement?.Parent is not BlockSyntax sendBlock
            || declaration.Initializer?.Value.RemoveParentheses() is not ObjectCreationExpressionSyntax creation)
        {
            return false;
        }

        var state = InitializerUsesPickupDirectory(creation.Initializer, model);
        var changes = sendBlock.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(x => x.SpanStart > declaration.Span.End
                        && x.SpanStart < send.SpanStart
                        && (ReferencesSymbol(x.Left, local, model) || IsDeliveryMethodOf(x.Left, local, model)))
            .OrderBy(x => x.SpanStart)
            .ToArray();
        foreach (var change in changes)
        {
            if (change.Parent is not ExpressionStatementSyntax { Parent: { } parent } || parent != sendBlock)
            {
                return false;
            }

            if (ReferencesSymbol(change.Left, local, model))
            {
                state = change.Right.RemoveParentheses() is ObjectCreationExpressionSyntax replacement
                        && InitializerUsesPickupDirectory(replacement.Initializer, model);
            }
            else
            {
                state = IsSpecifiedPickupDirectory(change.Right, model);
            }
        }

        return state && !HasPotentialMutation(sendBlock, declaration.Span.End, send.SpanStart, local, model);
    }

    private static bool InitializerUsesPickupDirectory(InitializerExpressionSyntax initializer, SemanticModel model) =>
        initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Any(x => model.GetSymbolInfo(x.Left).Symbol is IPropertySymbol { Name: "DeliveryMethod" }
                      && IsSpecifiedPickupDirectory(x.Right, model)) == true;

    private static bool IsDeliveryMethodOf(ExpressionSyntax left, ISymbol receiver, SemanticModel model) =>
        left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "DeliveryMethod" } memberAccess
        && model.GetSymbolInfo(memberAccess.Expression).Symbol is { } candidate
        && candidate.Equals(receiver);

    private static bool ReferencesSymbol(ExpressionSyntax expression, ISymbol symbol, SemanticModel model) =>
        model.GetSymbolInfo(expression).Symbol is { } candidate && candidate.Equals(symbol);

    private static bool HasPotentialMutation(SyntaxNode scope, int start, int end, ISymbol receiver, SemanticModel model) =>
        scope.DescendantNodes()
            .Where(x => x.SpanStart > start && x.SpanStart < end)
            .OfType<ArgumentSyntax>()
            .Any(x => !x.RefOrOutKeyword.IsKind(SyntaxKind.None) && ReferencesSymbol(x.Expression, receiver, model));

    private static bool FieldIsPickupDirectoryClient(IFieldSymbol field, SemanticModel model)
    {
        var declarator = field.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<VariableDeclaratorSyntax>()
            .SingleOrDefault();
        if (declarator?.Initializer?.Value.RemoveParentheses() is not ObjectCreationExpressionSyntax initializer
            || !InitializerUsesPickupDirectory(initializer.Initializer, model.Compilation.GetSemanticModel(declarator.SyntaxTree)))
        {
            return false;
        }

        foreach (var declaration in field.ContainingType.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<TypeDeclarationSyntax>())
        {
            var declarationModel = model.Compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var assignment in declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (ReferencesSymbol(assignment.Left, field, declarationModel)
                    || IsDeliveryMethodOf(assignment.Left, field, declarationModel)
                       && !IsSpecifiedPickupDirectory(assignment.Right, declarationModel))
                {
                    return false;
                }
            }

            if (declaration.DescendantNodes().OfType<ArgumentSyntax>().Any(x => ReferencesSymbol(x.Expression, field, declarationModel)))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsSpecifiedPickupDirectory(ExpressionSyntax expression, SemanticModel model) =>
        model.GetSymbolInfo(expression).Symbol is IFieldSymbol
        {
            Name: "SpecifiedPickupDirectory",
            ContainingType: { } containingType,
        }
        && containingType.ToDisplayString() == SmtpDeliveryMethodType;
}
