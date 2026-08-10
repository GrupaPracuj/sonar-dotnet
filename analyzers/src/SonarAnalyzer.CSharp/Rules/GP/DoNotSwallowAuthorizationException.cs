namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotSwallowAuthorizationException : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0021";

    private const string MessageFormat = "Do not silently swallow an exception around an access check - at least log the failure.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> AccessCheckMethods = new(StringComparer.Ordinal) { "HasClaim", "IsInRole" };
    private static readonly HashSet<string> LoggingMethods = new(StringComparer.Ordinal)
    {
        "Log",
        "LogTrace",
        "LogDebug",
        "LogInformation",
        "LogWarning",
        "LogError",
        "LogCritical",
        "Verbose",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Fatal",
        "Write",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeTryStatement, SyntaxKind.TryStatement);

    private static void AnalyzeTryStatement(SonarSyntaxNodeReportingContext context)
    {
        var tryStatement = (TryStatementSyntax)context.Node;
        if (!ContainsAccessCheck(tryStatement.Block))
        {
            return;
        }

        foreach (var catchClause in tryStatement.Catches)
        {
            if (catchClause.Block is not { } block
                || IsLogged(context.Model, catchClause, block)
                || AlwaysThrows(block)
                || (block.Statements.Count == 0 && IsCoveredByGenericCatchRule(catchClause, context.Model)))
            {
                continue;
            }

            context.ReportIssue(Rule, catchClause);
        }
    }

    private static bool IsLogged(SemanticModel model, CatchClauseSyntax catchClause, BlockSyntax block)
    {
        if (catchClause.Declaration is not { Identifier.RawKind: not 0 } declaration
            || model.GetDeclaredSymbol(declaration) is not { } exception)
        {
            return false;
        }

        return block.DescendantNodes(DoesNotEnterNestedFunction)
            .OfType<InvocationExpressionSyntax>()
            .Any(x => GpLoggingHelper.IsLoggingCall(model, x)
                      && model.GetSymbolInfo(x).Symbol is IMethodSymbol method
                      && LoggingMethods.Contains(method.Name)
                      && x.ArgumentList.Arguments.Any(argument => ReferencesSymbol(model, argument.Expression, exception)));
    }

    private static bool ReferencesSymbol(SemanticModel model, SyntaxNode node, ISymbol symbol) =>
        node.DescendantNodesAndSelf(DoesNotEnterNestedFunction)
            .OfType<IdentifierNameSyntax>()
            .Any(x => symbol.Equals(model.GetSymbolInfo(x).Symbol));

    private static bool DoesNotEnterNestedFunction(SyntaxNode node) =>
        node.Kind() != SyntaxKindEx.LocalFunctionStatement && node is not AnonymousFunctionExpressionSyntax;

    private static bool AlwaysThrows(BlockSyntax block) =>
        Outcomes(block.Statements) == FlowOutcome.Throws;

    private static FlowOutcome Outcomes(SyntaxList<StatementSyntax> statements)
    {
        var outcome = FlowOutcome.Continues;
        foreach (var statement in statements)
        {
            if (!outcome.HasFlag(FlowOutcome.Continues))
            {
                break;
            }

            outcome = (outcome & ~FlowOutcome.Continues) | Outcomes(statement);
        }

        return outcome;
    }

    private static FlowOutcome Outcomes(StatementSyntax statement)
    {
        if (statement.Kind() == SyntaxKindEx.LocalFunctionStatement)
        {
            return FlowOutcome.Continues;
        }

        return statement switch
        {
            EmptyStatementSyntax or ExpressionStatementSyntax or LocalDeclarationStatementSyntax => FlowOutcome.Continues,
            BlockSyntax block => Outcomes(block.Statements),
            ThrowStatementSyntax => FlowOutcome.Throws,
            ReturnStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax or GotoStatementSyntax or YieldStatementSyntax => FlowOutcome.OtherExit,
            IfStatementSyntax conditional => Outcomes(conditional.Statement)
                                             | (conditional.Else is null ? FlowOutcome.Continues : Outcomes(conditional.Else.Statement)),
            CheckedStatementSyntax checkedStatement => Outcomes(checkedStatement.Block.Statements),
            UnsafeStatementSyntax unsafeStatement => Outcomes(unsafeStatement.Block.Statements),
            LabeledStatementSyntax labeledStatement => Outcomes(labeledStatement.Statement),
            LockStatementSyntax lockStatement => Outcomes(lockStatement.Statement),
            UsingStatementSyntax usingStatement => Outcomes(usingStatement.Statement),
            _ => FlowOutcome.Unknown,
        };
    }

    [Flags]
    private enum FlowOutcome
    {
        Continues = 1,
        Throws = 2,
        OtherExit = 4,
        Unknown = 8,
    }

    // S2486 already reports an empty "catch" or "catch (Exception)" without a filter, so reporting those again here
    // would only produce a second issue on the same line. What S2486 deliberately leaves alone - an empty catch of a
    // specific exception type, or one behind a filter - is what this rule adds.
    private static bool IsCoveredByGenericCatchRule(CatchClauseSyntax catchClause, SemanticModel model) =>
        catchClause.Filter is null
        && (catchClause.Declaration?.Type is not { } type || model.GetTypeInfo(type).Type.Is(KnownType.System_Exception));

    private static bool ContainsAccessCheck(SyntaxNode node) =>
        node.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(x => x.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } && AccessCheckMethods.Contains(name));
}
