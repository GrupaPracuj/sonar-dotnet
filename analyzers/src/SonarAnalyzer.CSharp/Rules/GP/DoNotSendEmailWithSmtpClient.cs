namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotSendEmailWithSmtpClient : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0037";

    private const string MessageFormat = "Send email through Juno's email sender instead of '{0}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> SmtpTypes = new(StringComparer.Ordinal)
    {
        "System.Net.Mail.SmtpClient",
        "System.Web.Mail.SmtpMail",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && SmtpTypes.Contains(type.ToDisplayString()))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }
}
