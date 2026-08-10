namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateOnlyPropertyShouldNotBeNamedUtc : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0017";

    private const string MessageFormat = "Rename '{0}' - a date without a time component should not have 'Utc' in its name.";

    private const string JunoLocalDateType = "GP.Juno.Dates.LocalDate";
    private const string NodaLocalDateType = "NodaTime.LocalDate";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
        context.RegisterNodeAction(AnalyzeRecordParameters, SyntaxKindEx.RecordDeclaration, SyntaxKindEx.RecordStructDeclaration);
    }

    // A positional parameter of a record - class or struct - declares a public member, so the name reaches every consumer the same way.
    private static void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax declaration
            || !RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            || ((RecordDeclarationSyntaxWrapper)declaration).ParameterList is not { } parameterList)
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters.Where(x => x.Type is not null))
        {
            if (IsDateOnlyType(context.Model.GetTypeInfo(parameter.Type).Type) && GpIdentifierWords.ContainsWord(parameter.Identifier.ValueText, "Utc"))
            {
                context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText);
            }
        }
    }

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (IsDateOnlyType(context.Model.GetTypeInfo(declaration.Type).Type) && GpIdentifierWords.ContainsWord(declaration.Identifier.ValueText, "Utc"))
        {
            context.ReportIssue(Rule, declaration.Identifier, declaration.Identifier.ValueText);
        }
    }

    private static void AnalyzeField(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (FieldDeclarationSyntax)context.Node;
        if (!IsDateOnlyType(context.Model.GetTypeInfo(declaration.Declaration.Type).Type))
        {
            return;
        }

        foreach (var variable in declaration.Declaration.Variables)
        {
            if (GpIdentifierWords.ContainsWord(variable.Identifier.ValueText, "Utc"))
            {
                context.ReportIssue(Rule, variable.Identifier, variable.Identifier.ValueText);
            }
        }
    }

    private static bool IsDateOnlyType(ITypeSymbol type)
    {
        while (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } nullable
               && nullable.OriginalDefinition.Is(KnownType.System_Nullable_T))
        {
            type = nullable.TypeArguments[0];
        }

        return type is not null
               && (type.Is(KnownType.System_DateOnly)
                   || type.ToDisplayString() is JunoLocalDateType or NodaLocalDateType);
    }
}
