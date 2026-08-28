/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticConstructorShouldNotThrow : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0078";

    private const string MessageFormat = "Static constructors should not throw - it permanently poisons '{0}' for the rest of the process.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
        context.RegisterNodeAction(AnalyzeFieldInitializer, SyntaxKind.VariableDeclarator);
        context.RegisterNodeAction(AnalyzePropertyInitializer, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeConstructor(SonarSyntaxNodeReportingContext context)
    {
        var constructor = (ConstructorDeclarationSyntax)context.Node;
        if (!constructor.Modifiers.Any(SyntaxKind.StaticKeyword) || !ThrowsDirectly(context.Model, constructor))
        {
            return;
        }

        context.ReportIssue(Rule, constructor.Identifier, constructor.Identifier.ValueText);
    }

    private static void AnalyzeFieldInitializer(SonarSyntaxNodeReportingContext context)
    {
        var declarator = (VariableDeclaratorSyntax)context.Node;
        if (declarator.Initializer is { } initializer
            && context.Model.GetDeclaredSymbol(declarator) is IFieldSymbol { IsStatic: true } field
            && ContainsUncaughtThrow(context.Model, initializer))
        {
            context.ReportIssue(Rule, declarator.Identifier, field.ContainingType.Name);
        }
    }

    private static void AnalyzePropertyInitializer(SonarSyntaxNodeReportingContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (property.Initializer is { } initializer
            && context.Model.GetDeclaredSymbol(property) is IPropertySymbol { IsStatic: true } symbol
            && ContainsUncaughtThrow(context.Model, initializer))
        {
            context.ReportIssue(Rule, property.Identifier, symbol.ContainingType.Name);
        }
    }

    // A throw that runs synchronously as part of the type initializer - as opposed to one that only fires later,
    // from inside a local function or lambda the static constructor merely declares (e.g. assigns to a field to
    // run on demand) rather than calls right away while the type is being initialized.
    // "ExpressionBody" is ambiguous as a plain member access here: the CFG extension method and the generated shim
    // extension member both offer it on this project's compile-time (older) Roslyn reference, so the call is qualified.
    private static bool ThrowsDirectly(SemanticModel model, ConstructorDeclarationSyntax constructor)
    {
        SyntaxNode body = constructor.Body ?? (SyntaxNode)SonarAnalyzer.CFG.Extensions.BaseMethodDeclarationSyntaxExtensions.ExpressionBody(constructor);
        return body is not null && ContainsUncaughtThrow(model, body);
    }

    private static bool ContainsUncaughtThrow(SemanticModel model, SyntaxNode root) =>
        root.DescendantNodesAndSelf(DoesNotBelongToANestedFunction)
            .Where(IsThrow)
            .Any(x => !IsCaught(model, x));

    // A throw inside a lambda or local function exits that function, not the static constructor, so it does not
    // run while the type is being initialized and cannot poison the type.
    private static bool DoesNotBelongToANestedFunction(SyntaxNode node) =>
        node.Kind() != SyntaxKindEx.LocalFunctionStatement && node is not AnonymousFunctionExpressionSyntax;

    private static bool IsThrow(SyntaxNode node) =>
        node is ThrowStatementSyntax || node.Kind() == SyntaxKindEx.ThrowExpression;

    private static bool IsCaught(SemanticModel model, SyntaxNode throwNode)
    {
        var thrownExpression = throwNode switch
        {
            ThrowStatementSyntax throwStatement => throwStatement.Expression,
            _ when ThrowExpressionSyntaxWrapper.IsInstance(throwNode) => ((ThrowExpressionSyntaxWrapper)throwNode).Expression,
            _ => null
        };
        if (thrownExpression is null)
        {
            return IsCaughtByCatchAll(model, throwNode);
        }

        if (model.GetTypeInfo(thrownExpression).Type is not { } thrownType)
        {
            return false;
        }

        return throwNode.Ancestors()
            .OfType<TryStatementSyntax>()
            .Where(x => x.Block.Span.Contains(throwNode.Span))
            .SelectMany(x => x.Catches)
            .Any(x => x.Filter is null && Catches(model, x, thrownType));
    }

    private static bool IsCaughtByCatchAll(SemanticModel model, SyntaxNode throwNode) =>
        throwNode.Ancestors()
            .OfType<TryStatementSyntax>()
            .Where(x => x.Block.Span.Contains(throwNode.Span))
            .SelectMany(x => x.Catches)
            .Any(x => x.Filter is null
                      && (x.Declaration is null
                          || model.GetTypeInfo(x.Declaration.Type).Type.Is(KnownType.System_Exception)));

    private static bool Catches(SemanticModel model, CatchClauseSyntax catchClause, ITypeSymbol thrownType) =>
        catchClause.Declaration is null
        || (model.GetTypeInfo(catchClause.Declaration.Type).Type is { } caughtType
            && (thrownType.Equals(caughtType) || GpJunoTypes.DerivesFrom(thrownType, caughtType.ToDisplayString())));
}
