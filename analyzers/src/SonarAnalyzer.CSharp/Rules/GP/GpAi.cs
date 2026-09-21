/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// Shared symbol matching and local value resolution for GP0141, GP0143, GP0145, and GP0146.
internal static class GpAi
{
    private static readonly HashSet<string> ChatClientTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.AI.IChatClient",
    };
    private static readonly HashSet<string> ChatClientMethods = new(StringComparer.Ordinal)
    {
        "GetResponseAsync",
        "GetStreamingResponseAsync",
    };
    private static readonly HashSet<string> AgentTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Agents.AI.AIAgent",
        "Microsoft.Extensions.AI.AIAgent",
    };
    private static readonly HashSet<string> AgentMethods = new(StringComparer.Ordinal)
    {
        "RunAsync",
        "RunStreamingAsync",
    };
    private static readonly HashSet<string> ChatOptionsTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.AI.ChatOptions",
    };
    private static readonly HashSet<string> ChatResponseTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.AI.ChatResponse",
    };
    private static readonly HashSet<string> ChatMessageTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.AI.ChatMessage",
    };
    private static readonly HashSet<string> AgentResponseTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Agents.AI.AgentRunResponse",
        "Microsoft.Extensions.AI.AgentRunResponse",
    };
    private static readonly HashSet<string> UsageContentTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.AI.UsageContent",
    };
    private static readonly HashSet<string> ChatClientAgentTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Agents.AI.ChatClientAgent",
        "Microsoft.Extensions.AI.ChatClientAgent",
    };

    // ---- AI model invocation recognition -----------------------------------------------------------------------
    // Recognizes IChatClient.GetResponseAsync/GetStreamingResponseAsync and AIAgent.RunAsync/RunStreamingAsync
    // regardless of whether the call site goes through the interface/base type directly,
    // an interface implementation, an override of the abstract agent, or an extension method built on top of it.
    internal static bool IsModelInvocation(IMethodSymbol method) =>
        method is not null
        && (MatchesApi(method, ChatClientTypes, ChatClientMethods)
            || MatchesApi(method, AgentTypes, AgentMethods));

    private static bool MatchesApi(IMethodSymbol method, HashSet<string> types, HashSet<string> methods)
    {
        var reduced = method.ReducedFrom ?? method;
        if (!methods.Contains(reduced.Name))
        {
            return false;
        }

        return MatchesType(reduced, types);
    }

    private static bool MatchesType(IMethodSymbol method, HashSet<string> types)
    {
        if (method.ContainingType is { } containingType && types.Contains(containingType.OriginalDefinition.ToDisplayString()))
        {
            return true;
        }

        if (method.InterfaceMembers().Any(x => types.Contains(x.ContainingType.OriginalDefinition.ToDisplayString())))
        {
            return true;
        }

        for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
        {
            if (types.Contains(overridden.ContainingType.OriginalDefinition.ToDisplayString()))
            {
                return true;
            }
        }

        // The receiver parameter of an extension method may be declared through a more specific interface/base type
        // than the one configured (e.g. "this IMyChatClient client" where IMyChatClient : IChatClient), or through a
        // generic parameter constrained to it - either way, the receiver "implements/derives" the configured type
        // rather than being it, so the same walk used for a concrete receiver's own type (ImplementsOrDerivesConfiguredType)
        // applies here too, instead of requiring an exact name match.
        return method.IsExtensionMethod
               && method.Parameters.Length > 0
               && ImplementsOrDerivesType(method.Parameters[0].Type, types);
    }

    internal static bool IsChatOptions(ITypeSymbol type) => ImplementsOrDerivesType(type, ChatOptionsTypes);

    internal static bool IsChatResponse(ITypeSymbol type) => ImplementsOrDerivesType(type, ChatResponseTypes);

    internal static bool IsChatMessage(ITypeSymbol type) => ImplementsOrDerivesType(type, ChatMessageTypes);

    internal static bool IsAgentResponse(ITypeSymbol type) => ImplementsOrDerivesType(type, AgentResponseTypes);

    internal static bool IsUsageContent(ITypeSymbol type) => ImplementsOrDerivesType(type, UsageContentTypes);

    internal static bool IsChatClientAgent(ITypeSymbol type) => ImplementsOrDerivesType(type, ChatClientAgentTypes);

    // Shared "does this type implement or derive one of the configured types" walk: base type chain, all
    // interfaces (direct and inherited), and - for a generic type parameter, such as an extension method's own
    // "this T receiver" constrained to a configured interface - its constraint types, recursively. This is what lets
    // a concrete receiver type (or a generic receiver constrained to the right shape) match a configured interface
    // or base class without needing an exact name match.
    private static bool ImplementsOrDerivesType(ITypeSymbol type, HashSet<string> types)
    {
        if (type is null)
        {
            return false;
        }

        if (type is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.ConstraintTypes.Any(x => ImplementsOrDerivesType(x, types));
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (types.Contains(current.OriginalDefinition.ToDisplayString()))
            {
                return true;
            }
        }

        return type.AllInterfaces.Any(x => types.Contains(x.OriginalDefinition.ToDisplayString()));
    }

    // ---- argument/options resolution (single-hop, intraprocedural only) --------------------------------------
    // Finds the argument bound to a given parameter of the invoked method. Returns true and a null expression when
    // the parameter exists on the method but no argument was supplied at the call site (the parameter's default is
    // used) - callers treat that the same as an explicit null for an optional reference-typed parameter such as
    // ChatOptions.
    internal static bool TryFindArgument(InvocationExpressionSyntax invocation,
                                        IMethodSymbol method,
                                        Func<IParameterSymbol, bool> parameterPredicate,
                                        out ExpressionSyntax expression,
                                        out IParameterSymbol parameter)
    {
        expression = null;
        parameter = method?.Parameters.FirstOrDefault(parameterPredicate);
        if (parameter is null)
        {
            return false;
        }

        for (var i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
        {
            var argument = invocation.ArgumentList.Arguments[i];
            var candidate = argument.NameColon is { Name.Identifier.ValueText: var name }
                ? method.Parameters.FirstOrDefault(p => p.Name == name)
                : (i < method.Parameters.Length ? method.Parameters[i] : null);
            if (candidate is not null && candidate.Equals(parameter))
            {
                expression = argument.Expression;
                return true;
            }
        }

        return true; // Parameter exists but was not supplied - its default value applies.
    }

    // A single hop from a local variable to its own initializer - enough to see through "var options = new(...)"
    // immediately followed by a call, without attempting general-purpose dataflow analysis.
    internal static ExpressionSyntax ResolveLocalValue(SemanticModel model, ExpressionSyntax expression)
    {
        expression = (ExpressionSyntax)expression?.RemoveParentheses();
        if (expression is IdentifierNameSyntax
            && model.GetSymbolInfo(expression).Symbol is ILocalSymbol local
            && local.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<VariableDeclaratorSyntax>().FirstOrDefault() is { Initializer.Value: { } initializerValue })
        {
            return (ExpressionSyntax)initializerValue.RemoveParentheses();
        }

        return expression;
    }

    internal static IObjectCreation AsObjectCreation(SyntaxNode node) =>
        ObjectCreationFactory.TryCreate(node);

    internal static ExpressionSyntax PropertyAssignmentValue(IObjectCreation creation, string propertyName) =>
        creation?.InitializerExpressions
            ?.OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(x => x.Left is IdentifierNameSyntax { Identifier.ValueText: { } name } && name == propertyName)
            ?.Right;

    // Recognizes a null literal, unwrapping surrounding parentheses and any leading cast ("(ChatOptions)null").
    internal static bool IsNullLiteral(ExpressionSyntax expression)
    {
        expression = (ExpressionSyntax)expression?.RemoveParentheses();
        while (expression is CastExpressionSyntax cast)
        {
            expression = (ExpressionSyntax)cast.Expression.RemoveParentheses();
        }

        return expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression };
    }

    // Resolves the value effectively assigned to a property of an options-like object (ChatOptions,
    // JsonSerializerOptions, ...) at a given usage site (the call whose argument is the options expression). The
    // "base" object to inspect is the most recent one the local was ever set to before the usage site: either a
    // later whole-variable replacement ("options = new ChatOptions(); ..." or "options = CreateOptions(); ..."),
    // when one is found on the same safe path to the usage (see StatementsOnPathToUsage), or - failing that - the
    // declaration's own initializer. From that base, two provable shapes contribute, most-recent-wins: the base
    // object's own inline initializer, when it is itself an object creation, and a direct "local.Property = value;"
    // statement proven to run after the base was established and before the usage site. This mirrors the pattern
    // DoNotSendEmailWithSmtpClient already uses for "was this property still set to X right before the call".
    //
    // Returns false - nothing provable, callers should accept rather than guess - both when the options expression
    // is not a local at all and does not resolve to an inline object creation (a parameter, a field, a method call),
    // and when the base object is itself unprovable (an opaque replacement or initializer, e.g.
    // "options = CreateOptions();") and no direct property assignment after it was found either: an opaque value
    // proves nothing about whether the property ends up set, so it must not be treated the same as a proven-empty
    // object creation. Returns true otherwise, with 'value' set to the resolved right-hand expression, or null when
    // the base object was proven (an inline object creation) but the property was genuinely never set anywhere
    // provable - a real "missing" finding. This also means a later replacement with a fresh, empty object creation
    // correctly overrides an earlier, compliant one - the local now points at a different object entirely.
    internal static bool TryResolveEffectivePropertyValue(SemanticModel model,
                                                          ExpressionSyntax optionsExpression,
                                                          SyntaxNode usageSite,
                                                          string propertyName,
                                                          out ExpressionSyntax value)
    {
        value = null;
        if (optionsExpression is null)
        {
            return false;
        }

        var expression = (ExpressionSyntax)optionsExpression.RemoveParentheses();
        var local = expression is IdentifierNameSyntax && model.GetSymbolInfo(expression).Symbol is ILocalSymbol localSymbol ? localSymbol : null;

        ExpressionSyntax baseValue;
        int basePosition;
        if (local is not null && LastLocalReplacementBeforeUsage(model, local, usageSite) is { } replacement)
        {
            baseValue = replacement.Value;
            basePosition = replacement.Position;
        }
        else
        {
            baseValue = ResolveLocalValue(model, expression);
            basePosition = local?.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<VariableDeclaratorSyntax>().FirstOrDefault()
                                 ?.FirstAncestorOrSelf<StatementSyntax>()?.Span.End ?? -1;
        }

        // The local's most recent value before the usage is provably null (an explicit "= null;" declaration or a
        // later whole-variable replacement, including through a cast) - passing it on is exactly the same as passing
        // null directly to the call, and must be reported the same way (a proven-missing options object).
        if (IsNullLiteral(baseValue))
        {
            return true;
        }

        var creation = AsObjectCreation(baseValue);
        var mutatedValue = local is not null ? LastDirectAssignmentAfter(model, local, usageSite, propertyName, basePosition) : null;

        if (creation is null && mutatedValue is null)
        {
            return false;
        }

        value = mutatedValue ?? PropertyAssignmentValue(creation, propertyName);
        return true;
    }

    // The statements between the local's own declaration and the usage site that are reached along a single,
    // uninterrupted path of nested blocks - the usage's own block, its parent block, its parent's parent, and so on
    // up to the block that directly contains the declaration - in program order. Even when the local was declared
    // in an outer block and a statement of interest (a whole-variable replacement, a property mutation) sits inside
    // a nested block (an "if" body, for instance) alongside the usage itself, both are still on this single path and
    // are guaranteed to run, in order, before the usage is reached, regardless of whatever branch or loop the outer
    // blocks along the path happen to belong to. A statement reached only through a different branch (e.g. one in
    // an "if" body when the usage sits in the matching "else") is not on that path and is correctly excluded. When
    // the usage cannot be shown to sit on such a path at all - which should not happen for a local actually in scope
    // there - this yields nothing rather than guessing.
    private static IEnumerable<StatementSyntax> StatementsOnPathToUsage(ILocalSymbol local, SyntaxNode usageSite)
    {
        if (local.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<VariableDeclaratorSyntax>().FirstOrDefault() is not { } declarator
            || declarator.FirstAncestorOrSelf<StatementSyntax>() is not { } declarationStatement
            || declarationStatement.Parent is not BlockSyntax declarationBlock)
        {
            yield break;
        }

        var chain = new List<BlockSyntax>();
        for (var block = usageSite.Ancestors().OfType<BlockSyntax>().FirstOrDefault(); block is not null; block = block.Ancestors().OfType<BlockSyntax>().FirstOrDefault())
        {
            chain.Add(block);
            if (block == declarationBlock)
            {
                break;
            }
        }

        if (chain.Count == 0 || chain[chain.Count - 1] != declarationBlock)
        {
            yield break; // The usage is not reachable through simple block nesting from the declaration - do not guess.
        }

        chain.Reverse(); // Outermost (the declaration's own block) first, innermost (the usage's own block) last.

        for (var i = 0; i < chain.Count; i++)
        {
            var block = chain[i];
            var lowerBound = i == 0 ? declarationStatement.Span.End : block.SpanStart;
            var upperBound = i == chain.Count - 1
                ? usageSite.SpanStart
                : block.Statements.FirstOrDefault(x => x.Span.Contains(chain[i + 1].SpanStart))?.SpanStart ?? usageSite.SpanStart;

            foreach (var statement in block.Statements)
            {
                if (statement.SpanStart <= lowerBound || statement.SpanStart >= upperBound)
                {
                    continue;
                }

                yield return statement;
            }
        }
    }

    // The last whole-variable replacement ("local = value;") found on the safe path to the usage, if any - i.e. the
    // local was pointed at an entirely different object, not just had one of its properties changed.
    private static (ExpressionSyntax Value, int Position)? LastLocalReplacementBeforeUsage(SemanticModel model, ILocalSymbol local, SyntaxNode usageSite)
    {
        (ExpressionSyntax Value, int Position)? result = null;
        foreach (var statement in StatementsOnPathToUsage(local, usageSite))
        {
            if (statement is ExpressionStatementSyntax
                {
                    Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression, Left: IdentifierNameSyntax } assignment
                }
                && model.GetSymbolInfo(assignment.Left).Symbol is { } leftSymbol
                && leftSymbol.Equals(local))
            {
                result = ((ExpressionSyntax)assignment.Right.RemoveParentheses(), statement.SpanStart);
            }
        }

        return result;
    }

    // The last direct "local.Property = value;" statement found on the safe path to the usage, restricted to
    // statements strictly after 'afterPosition' - the point where the base object being inspected (the declaration's
    // initializer, or a later whole-variable replacement) was established. A mutation that happened before that
    // point applied to a since-replaced object and is not relevant to the current one.
    private static ExpressionSyntax LastDirectAssignmentAfter(SemanticModel model, ILocalSymbol local, SyntaxNode usageSite, string propertyName, int afterPosition)
    {
        ExpressionSyntax result = null;
        foreach (var statement in StatementsOnPathToUsage(local, usageSite))
        {
            if (statement.SpanStart <= afterPosition)
            {
                continue;
            }

            if (DirectAssignmentValue(model, statement, local, propertyName) is { } value)
            {
                result = value;
            }
        }

        return result;
    }

    private static ExpressionSyntax DirectAssignmentValue(SemanticModel model, StatementSyntax statement, ILocalSymbol local, string propertyName) =>
        statement is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax
            {
                Left: MemberAccessExpressionSyntax { Name.Identifier.ValueText: { } name } memberAccess
            } assignment
        }
        && name == propertyName
        && model.GetSymbolInfo(memberAccess.Expression).Symbol is { } receiver
        && receiver.Equals(local)
            ? assignment.Right
            : null;
}
