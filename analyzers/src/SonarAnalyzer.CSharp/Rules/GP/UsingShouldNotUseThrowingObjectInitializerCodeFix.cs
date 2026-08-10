using Microsoft.CodeAnalysis.Formatting;

namespace SonarAnalyzer.CSharp.Rules;

// Only the "using var x = new Foo { ... };" declaration form is fixed. Rewriting the "using (var x = new Foo { ... }) { ... }"
// statement form correctly means either turning it into a using-declaration that now scopes the rest of the block, or
// preserving the original block as a nested scope - both are structurally riskier than this mechanical rewrite is meant to be,
// so that shape is reported by the analyzer but left for the developer to fix by hand.
[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class UsingShouldNotUseThrowingObjectInitializerCodeFix : SonarCodeFix
{
    internal const string Title = "Move the member assignments out of the object initializer";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(UsingShouldNotUseThrowingObjectInitializer.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var span = diagnostic.Location.SourceSpan;

        if (root.FindNode(span) is not ObjectCreationExpressionSyntax { Initializer: { } initializer } objectCreation
            || objectCreation.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable }
            || variable.Parent is not VariableDeclarationSyntax { Variables.Count: 1 }
            || variable.Parent.Parent is not LocalDeclarationStatementSyntax { UsingKeyword: { RawKind: (int)SyntaxKind.UsingKeyword } } localDeclaration
            || localDeclaration.Parent is not BlockSyntax block)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (model is null
            || !initializer.Expressions.All(IsMovableMemberAssignment)
            || initializer.Expressions.OfType<AssignmentExpressionSyntax>()
                .Any(x => IsInitializerOnlyMember(model.GetSymbolInfo(x.Left).Symbol)))
        {
            return;
        }

        context.RegisterCodeFix(
            Title,
            _ =>
            {
                var bareObjectCreation = objectCreation
                    .WithArgumentList(objectCreation.ArgumentList ?? SyntaxFactory.ArgumentList())
                    .WithInitializer(null);
                var newLocalDeclaration = localDeclaration.ReplaceNode(objectCreation, bareObjectCreation).WithAdditionalAnnotations(Formatter.Annotation);

                var receiver = SyntaxFactory.IdentifierName(variable.Identifier.ValueText);
                var assignmentStatements = initializer.Expressions
                    .OfType<AssignmentExpressionSyntax>()
                    .Select(assignment => SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, (SimpleNameSyntax)assignment.Left),
                                assignment.Right))
                        .WithLeadingTrivia(localDeclaration.GetLeadingTrivia())
                        .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed)
                        .WithAdditionalAnnotations(Formatter.Annotation))
                    .ToArray<StatementSyntax>();

                var statementIndex = block.Statements.IndexOf(localDeclaration);
                var newStatements = block.Statements.Replace(localDeclaration, newLocalDeclaration).InsertRange(statementIndex + 1, assignmentStatements);
                var newBlock = block.WithStatements(newStatements);
                var newRoot = root.ReplaceNode(block, newBlock);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }

    // Only a plain "Member = value" element becomes a valid "x.Member = value;" statement. A nested initializer
    // ("Items = { 1, 2 }") has no statement form at all, and an indexer element ("[key] = value") is not a member
    // name - both are still reported, but rewriting them is left to the developer.
    private static bool IsMovableMemberAssignment(ExpressionSyntax element) =>
        element is AssignmentExpressionSyntax { Left: SimpleNameSyntax, Right: not InitializerExpressionSyntax };

    private static bool IsInitOnly(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(x => x.AccessorList?.Accessors.AnyOfKind(SyntaxKindEx.InitAccessorDeclaration) == true);

    private static bool IsInitializerOnlyMember(ISymbol symbol) =>
        symbol switch
        {
            IPropertySymbol property => property.IsRequired() || IsInitOnly(property),
            IFieldSymbol field => field.DeclaringSyntaxReferences
                .Select(x => x.GetSyntax().FirstAncestorOrSelf<FieldDeclarationSyntax>())
                .WhereNotNull()
                .Any(x => x.Modifiers.Any(y => y.ValueText == "required")),
            _ => false,
        };
}
