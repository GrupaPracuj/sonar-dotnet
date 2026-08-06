using System.Globalization;
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

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var newMembers = enumDeclaration.Members.Select(member =>
                {
                    if (member.EqualsValue is not null)
                    {
                        return member;
                    }

                    var symbol = model.GetDeclaredSymbol(member) as IFieldSymbol;
                    var literal = CreateValueLiteral(symbol?.ConstantValue);
                    var equalsValue = SyntaxFactory.EqualsValueClause(literal).WithAdditionalAnnotations(Formatter.Annotation);
                    return member.WithEqualsValue(equalsValue);
                });
                var newEnum = enumDeclaration.WithMembers(SyntaxFactory.SeparatedList(newMembers, enumDeclaration.Members.GetSeparators()));
                var newRoot = root.ReplaceNode(enumDeclaration, newEnum);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }

    // SyntaxFactory.Literal(long) prints a numeric suffix ("0L") to keep the token's declared value type intact.
    // An enum member initializer almost always fits an int - the same digits without a suffix - which is what the
    // compliant example shows, and remains implicitly convertible to whatever the enum's actual underlying type is.
    // Only a value that overflows int (an underlying long/ulong enum with a huge member) needs the wider literal.
    private static LiteralExpressionSyntax CreateValueLiteral(object value)
    {
        try
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(Convert.ToInt32(value, CultureInfo.InvariantCulture)));
        }
        catch (OverflowException)
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(Convert.ToInt64(value, CultureInfo.InvariantCulture)));
        }
    }
}
