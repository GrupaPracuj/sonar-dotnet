/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpActionShouldDeclareErrorResponseType : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0103";

    private const string MessageFormat = "Declare the concrete response type for status {0} with ProducesResponseType.";

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
        var missingTypes = GpOpenApiMetadata.ReturnedInvocations(declaration)
            .Select(x => (Invocation: x, StatusCode: GpOpenApiMetadata.ResponseStatusCode(context.Model, x)))
            .Where(x => x.StatusCode is >= 400 and <= 599
                        && documented.Contains(x.StatusCode.Value)
                        && GpOpenApiMetadata.HasPayload(context.Model, x.Invocation)
                        && !GpOpenApiMetadata.HasConcreteResponseTypeForStatus(method, x.StatusCode.Value))
            .ToArray();
        if (missingTypes.Length > 0)
        {
            var statuses = missingTypes.Select(x => x.StatusCode.Value).Distinct().OrderBy(x => x);
            context.ReportIssue(Rule, declaration.Identifier, missingTypes.Select(x => x.Invocation).ToSecondaryLocations(), string.Join(", ", statuses));
        }
    }
}
