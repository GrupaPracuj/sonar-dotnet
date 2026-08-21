/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpActionShouldDocumentResponseStatusCodes : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0100";

    private const string MessageFormat = "HTTP status {0} is returned but not declared. Add ProducesResponseType for this status.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is not { } method
            || !GpOpenApiMetadata.IsOpenApiAction(method)
            || GpOpenApiMetadata.IsIgnored(method)
            || GpOpenApiMetadata.UsesApiConvention(method))
        {
            return;
        }

        var documented = GpOpenApiMetadata.ResponseAttributes(method)
            .Select(GpOpenApiMetadata.ResponseStatusCode)
            .WhereNotNull()
            .ToHashSet();
        var missing = GpOpenApiMetadata.ReturnedInvocations(declaration)
            .Select(x => (Invocation: x, Status: GpOpenApiMetadata.ResponseStatusCode(context.Model, x)))
            .Where(x => x.Status is { } status && !documented.Contains(status))
            .Select(x => (x.Invocation, Status: x.Status.Value))
            .GroupBy(x => x.Status)
            .Select(x => x.First())
            .OrderBy(x => x.Status)
            .ToArray();
        foreach (var returned in missing)
        {
            context.ReportIssue(Rule, returned.Invocation, returned.Status.ToString());
        }
    }
}
