using Microsoft.CodeAnalysis.Formatting;

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class TestMethodShouldHaveTestAttributeCodeFix : SonarCodeFix
{
    internal const string Title = "Add the test attribute used by sibling tests";

    // Theory/TestCase/TestCaseSource require arguments that cannot be mechanically invented, so only the
    // parameterless test attributes are candidates to copy onto the flagged method.
    private static readonly HashSet<string> ParameterlessTestAttributes = new(StringComparer.Ordinal)
    {
        "TestMethod", "DataTestMethod", "Fact", "Test",
    };

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(TestMethodShouldHaveTestAttribute.RuleId);

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } flaggedMethod
            || flaggedMethod.Parent is not ClassDeclarationSyntax classDeclaration
            || FindSiblingAttribute(classDeclaration, flaggedMethod) is not { } attributeName)
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var newAttributeList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attributeName))))
                    .WithAdditionalAnnotations(Formatter.Annotation);
                var newMethod = flaggedMethod.WithAttributeLists(flaggedMethod.AttributeLists.Add(newAttributeList));
                var newRoot = root.ReplaceNode(flaggedMethod, newMethod);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);

        return Task.CompletedTask;
    }

    private static string FindSiblingAttribute(ClassDeclarationSyntax classDeclaration, MethodDeclarationSyntax flaggedMethod) =>
        classDeclaration.Members.OfType<MethodDeclarationSyntax>()
            .Where(x => x != flaggedMethod)
            .SelectMany(x => x.AttributeLists.SelectMany(list => list.Attributes))
            .Select(x => x.Name.ToString())
            .Select(x => x.EndsWith("Attribute", StringComparison.Ordinal) ? x.Substring(0, x.Length - "Attribute".Length) : x)
            .FirstOrDefault(ParameterlessTestAttributes.Contains);
}
