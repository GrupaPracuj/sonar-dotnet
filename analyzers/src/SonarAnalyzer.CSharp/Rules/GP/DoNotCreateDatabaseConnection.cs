namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotCreateDatabaseConnection : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0035";

    private const string MessageFormat = "Obtain the connection from Juno (IAdoConnectionFactory / IDbExecute) instead of constructing '{0}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && GpJunoTypes.DerivesFrom(type, "System.Data.Common.DbConnection"))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }
}
