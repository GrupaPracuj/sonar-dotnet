using Microsoft.CodeAnalysis.Formatting;

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class ContractEnumShouldAssignExplicitValuesCodeFix : SonarCodeFix
{
    internal const string Title = "Assign explicit values to all members";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ContractEnumShouldAssignExplicitValues.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<EnumDeclarationSyntax>() is not { } enumDeclaration)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (model is null)
        {
            return;
        }

        var implicitValues = new Dictionary<EnumMemberDeclarationSyntax, LiteralExpressionSyntax>();
        foreach (var member in enumDeclaration.Members.Where(x => x.EqualsValue is null))
        {
            if (model.GetDeclaredSymbol(member) is not IFieldSymbol { HasConstantValue: true, ConstantValue: { } value }
                || CreateValueLiteral(value) is not { } literal)
            {
                return;
            }

            implicitValues.Add(member, literal);
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var newMembers = enumDeclaration.Members.Select(member =>
                {
                    if (!implicitValues.TryGetValue(member, out var literal))
                    {
                        return member;
                    }

                    var equalsValue = SyntaxFactory.EqualsValueClause(literal).WithAdditionalAnnotations(Formatter.Annotation);
                    return member.WithEqualsValue(equalsValue);
                });
                var newEnum = enumDeclaration.WithMembers(SyntaxFactory.SeparatedList(newMembers, enumDeclaration.Members.GetSeparators()));
                var newRoot = root.ReplaceNode(enumDeclaration, newEnum);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }

    private static LiteralExpressionSyntax CreateValueLiteral(object value) =>
        value switch
        {
            byte number => Numeric(SyntaxFactory.Literal((int)number)),
            sbyte number => Numeric(SyntaxFactory.Literal((int)number)),
            short number => Numeric(SyntaxFactory.Literal((int)number)),
            ushort number => Numeric(SyntaxFactory.Literal((int)number)),
            int number => Numeric(SyntaxFactory.Literal(number)),
            uint number when number <= int.MaxValue => Numeric(SyntaxFactory.Literal((int)number)),
            uint number => Numeric(SyntaxFactory.Literal(number)),
            long number when number is >= int.MinValue and <= int.MaxValue => Numeric(SyntaxFactory.Literal((int)number)),
            long number => Numeric(SyntaxFactory.Literal(number)),
            ulong number when number <= int.MaxValue => Numeric(SyntaxFactory.Literal((int)number)),
            ulong number => Numeric(SyntaxFactory.Literal(number)),
            _ => null,
        };

    private static LiteralExpressionSyntax Numeric(SyntaxToken token) =>
        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, token);
}
