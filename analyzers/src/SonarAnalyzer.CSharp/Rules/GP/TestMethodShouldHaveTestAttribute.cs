/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestMethodShouldHaveTestAttribute : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0041";

    private const string MessageFormat = "Add a test attribute to '{0}' or make it private - as it stands it never runs.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> TestAttributeNames = new(StringComparer.Ordinal)
    {
        "TestMethod", "DataTestMethod",   // MSTest
        "Fact", "Theory",                 // xUnit (Theory is also NUnit)
        "Test", "TestCase", "TestCaseSource", // NUnit
    };

    // xUnit expresses teardown through IDisposable rather than an attribute, so Dispose has no attribute to look for.
    private static readonly HashSet<string> LifecycleMethodNames = new(StringComparer.Ordinal)
    {
        "Dispose",
        "DisposeAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);

    private static void AnalyzeClass(SonarSyntaxNodeReportingContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var methods = classDeclaration.Members.OfType<MethodDeclarationSyntax>().ToList();

        // Only a class that already contains a recognized test is examined, so a plain helper class is never reported.
        if (!methods.Any(x => HasTestAttribute(context.Model, x)))
        {
            return;
        }

        foreach (var method in methods.Where(x => LooksLikeAnUnannotatedTest(context.Model, x)))
        {
            context.ReportIssue(Rule, method.Identifier, method.Identifier.ValueText);
        }
    }

    // A public, parameterless, non-static method returning void or Task with no attributes at all: a test in every
    // framework, and an unusual shape for a helper. Any attribute at all means the author declared an intent
    // (a lifecycle hook, an explicit exclusion), so the method is left alone.
    //
    // A method that implements an interface member is left alone too: the interface, not an attribute, is what makes
    // the framework call it. That is how xUnit expresses setup and teardown - IAsyncLifetime.InitializeAsync/
    // DisposeAsync, IDisposable.Dispose - and it covers a project's own fixture interfaces just as well.
    private static bool LooksLikeAnUnannotatedTest(SemanticModel model, MethodDeclarationSyntax method) =>
        method.AttributeLists.Count == 0
        && method.ParameterList.Parameters.Count == 0
        && !LifecycleMethodNames.Contains(method.Identifier.ValueText)
        && model.GetDeclaredSymbol(method) is
        {
            DeclaredAccessibility: Accessibility.Public,
            IsStatic: false,
            IsOverride: false,
            IsAbstract: false,
            ExplicitInterfaceImplementations.IsEmpty: true,
            TypeParameters.Length: 0,
        } symbol
        && ReturnsVoidOrTask(symbol)
        && !symbol.InterfaceMembers().Any();

    private static bool ReturnsVoidOrTask(IMethodSymbol method) =>
        method.ReturnsVoid
        || method.ReturnType.Is(KnownType.System_Threading_Tasks_Task)
        || method.ReturnType.Is(KnownType.System_Threading_Tasks_ValueTask);

    private static bool HasTestAttribute(SemanticModel model, MethodDeclarationSyntax method) =>
        model.GetDeclaredSymbol(method) is { } symbol
        && symbol.GetAttributes().Any(x => x.AttributeClass?.Name is { } name && TestAttributeNames.Contains(TrimAttributeSuffix(name)));

    private static string TrimAttributeSuffix(string name) =>
        name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Attribute".Length)
            : name;
}
