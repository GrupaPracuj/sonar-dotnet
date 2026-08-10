namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationShouldNotBeSuppressed : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0054";

    private const string MessageFormat = "Do not turn cancellation into success - let '{0}' propagate or rethrow it.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> CancellationExceptions = new(StringComparer.Ordinal)
    {
        "System.OperationCanceledException",
        "System.Threading.Tasks.TaskCanceledException",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);

    private static void AnalyzeCatchClause(SonarSyntaxNodeReportingContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Declaration?.Type is not { } typeSyntax
            || context.Model.GetTypeInfo(typeSyntax).Type is not { } caught
            || !CancellationExceptions.Contains(caught.ToDisplayString())
            || DistinguishesTimeoutFromCancellation(context.Model, catchClause.Filter)
            || !IsKnownToSuppressCancellation(context.Model, catchClause.Block))
        {
            return;
        }

        context.ReportIssue(Rule, typeSyntax, caught.Name);
    }

    // "catch (TaskCanceledException) when (!token.IsCancellationRequested)" is the documented way to tell an HttpClient
    // timeout apart from a caller's cancellation: the filter only lets the exception through when nobody asked to stop,
    // so handling it there hides no cancellation signal. Any other filter still leaves the signal suppressed.
    private static bool DistinguishesTimeoutFromCancellation(SemanticModel model, CatchFilterClauseSyntax filter) =>
        filter?.FilterExpression is { } condition
        && GuaranteesCancellationWasNotRequested(model, condition);

    private static bool GuaranteesCancellationWasNotRequested(SemanticModel model, ExpressionSyntax condition)
    {
        while (condition is ParenthesizedExpressionSyntax parenthesized)
        {
            condition = parenthesized.Expression;
        }

        return condition switch
        {
            PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression) =>
                IsCancellationRequested(model, unary.Operand),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) =>
                GuaranteesCancellationWasNotRequested(model, binary.Left)
                || GuaranteesCancellationWasNotRequested(model, binary.Right),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression) =>
                GuaranteesCancellationWasNotRequested(model, binary.Left)
                && GuaranteesCancellationWasNotRequested(model, binary.Right),
            _ => false,
        };
    }

    private static bool IsCancellationRequested(SemanticModel model, ExpressionSyntax expression) =>
        model.GetSymbolInfo(expression.RemoveParentheses()).Symbol is IPropertySymbol { Name: "IsCancellationRequested" } property
        && property.ContainingType.Is(KnownType.System_Threading_CancellationToken);

    private static bool IsKnownToSuppressCancellation(SemanticModel model, BlockSyntax block)
    {
        var outcomes = block is null ? FlowOutcome.Unknown : Outcomes(model, block.Statements);
        return !outcomes.HasFlag(FlowOutcome.Unknown) && outcomes != FlowOutcome.CancellationThrow;
    }

    private static FlowOutcome Outcomes(SemanticModel model, SyntaxList<StatementSyntax> statements)
    {
        var outcome = FlowOutcome.Continues;
        foreach (var statement in statements)
        {
            if (!outcome.HasFlag(FlowOutcome.Continues))
            {
                break;
            }
            outcome = (outcome & ~FlowOutcome.Continues) | Outcomes(model, statement);
        }
        return outcome;
    }

    private static FlowOutcome Outcomes(SemanticModel model, StatementSyntax statement)
    {
        if (statement.Kind() == SyntaxKindEx.LocalFunctionStatement)
        {
            return FlowOutcome.Continues;
        }

        return statement switch
        {
            EmptyStatementSyntax or ExpressionStatementSyntax or LocalDeclarationStatementSyntax => FlowOutcome.Continues,
            BlockSyntax block => Outcomes(model, block.Statements),
            ThrowStatementSyntax throwStatement => IsCancellationThrow(model, throwStatement) ? FlowOutcome.CancellationThrow : FlowOutcome.OtherExit,
            ReturnStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax or GotoStatementSyntax or YieldStatementSyntax => FlowOutcome.OtherExit,
            IfStatementSyntax ifStatement => Outcomes(model, ifStatement.Statement)
                                             | (ifStatement.Else is null ? FlowOutcome.Continues : Outcomes(model, ifStatement.Else.Statement)),
            CheckedStatementSyntax checkedStatement => Outcomes(model, checkedStatement.Block.Statements),
            UnsafeStatementSyntax unsafeStatement => Outcomes(model, unsafeStatement.Block.Statements),
            LabeledStatementSyntax labeledStatement => Outcomes(model, labeledStatement.Statement),
            LockStatementSyntax lockStatement => Outcomes(model, lockStatement.Statement),
            UsingStatementSyntax usingStatement => Outcomes(model, usingStatement.Statement),
            FixedStatementSyntax fixedStatement => Outcomes(model, fixedStatement.Statement),
            _ => FlowOutcome.Unknown
        };
    }

    private static bool IsCancellationThrow(SemanticModel model, ThrowStatementSyntax throwStatement)
    {
        if (throwStatement.Expression is null)
        {
            return true;
        }

        return model.GetTypeInfo(throwStatement.Expression).Type is { } thrownType
               && CancellationExceptions.Any(x => GpJunoTypes.DerivesFrom(thrownType, x));
    }

    [Flags]
    private enum FlowOutcome
    {
        Continues = 1,
        CancellationThrow = 2,
        OtherExit = 4,
        Unknown = 8,
    }
}
