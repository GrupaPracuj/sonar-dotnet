/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using System.Collections.Concurrent;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageContractsShouldFollowConventions : SonarDiagnosticAnalyzer
{
    internal const string EventSuffixRuleId = "GP0002";
    internal const string CommandSuffixRuleId = "GP0003";
    internal const string BehaviorFreeMessageRuleId = "GP0004";

    private const string EventSuffixMessage = "Rename event '{0}' to remove the 'Event' suffix.";
    private const string CommandSuffixMessage = "Rename command '{0}' to remove the 'Command' suffix.";
    private const string BehaviorFreeMessageFormat = "Message contract '{0}' should not contain business behavior.";

    private static readonly DiagnosticDescriptor EventSuffixRule = DescriptorFactory.Create(EventSuffixRuleId, EventSuffixMessage);
    private static readonly DiagnosticDescriptor CommandSuffixRule = DescriptorFactory.Create(CommandSuffixRuleId, CommandSuffixMessage);
    private static readonly DiagnosticDescriptor BehaviorFreeMessageRule = DescriptorFactory.Create(BehaviorFreeMessageRuleId, BehaviorFreeMessageFormat);

    private static readonly HashSet<string> EventMessageMethods = new(StringComparer.Ordinal)
    {
        "Publishes",
        "Publish",
        "PublishAsync",
        "PublishBatch",
    };

    private static readonly HashSet<string> CommandMessageMethods = new(StringComparer.Ordinal)
    {
        "Sends",
        "Send"
    };

    private static readonly HashSet<string> MessagingMethods = new(EventMessageMethods.Concat(CommandMessageMethods), StringComparer.Ordinal);

    private static readonly HashSet<string> MessagePostfixes = new(StringComparer.Ordinal)
    {
        "Event",
        "Command"
    };

    // Members every contract may legitimately declare: these describe the value itself, not business behavior.
    private static readonly HashSet<string> ValueSemanticsMethods = new(StringComparer.Ordinal)
    {
        "Equals",
        "GetHashCode",
        "ToString",
        "Deconstruct",
        "Clone"
    };

    private static readonly HashSet<string> DisplayFormattingHelperMethods = new(StringComparer.Ordinal)
    {
        "AsString"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(EventSuffixRule, CommandSuffixRule, BehaviorFreeMessageRule);

    private enum MessageContractUse
    {
        None,
        Event,
        Command,
    }

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var behaviorMethods = new ConcurrentDictionary<string, IMethodSymbol>(StringComparer.Ordinal);
            start.RegisterNodeAction(c => AnalyzeInvocation(c, behaviorMethods), SyntaxKind.InvocationExpression);
            start.RegisterCompilationEndAction(c => ReportBehaviorMethods(c, behaviorMethods.Values));
        });

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, IMethodSymbol> behaviorMethods)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || !TryGetMessageInvocation(context.Model, invocation, out var messageType, out var reportNode, out var messageUse))
        {
            return;
        }

        var matchedPostfix = MessagePostfixes.FirstOrDefault(x => messageType.Name.EndsWith(x, StringComparison.Ordinal));
        if (matchedPostfix == "Event" && messageUse == MessageContractUse.Event)
        {
            context.ReportIssue(EventSuffixRule, reportNode, messageType.Name);
        }
        else if (matchedPostfix == "Command" && messageUse == MessageContractUse.Command)
        {
            context.ReportIssue(CommandSuffixRule, reportNode, messageType.Name);
        }

        var methods = BusinessBehaviorMethods(context.Model.Compilation, messageType).ToArray();
        var methodsWithSource = methods.Where(x => MethodIdentifier(x) is not null).ToArray();
        foreach (var method in methodsWithSource)
        {
            behaviorMethods.TryAdd(method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), method);
        }

        // Metadata does not retain source locations. Keep the registration as a fallback for contracts referenced
        // from a compiled assembly, while source contracts are reported directly on every offending method.
        if (methods.Length > 0 && methodsWithSource.Length == 0)
        {
            context.ReportIssue(BehaviorFreeMessageRule, reportNode, messageType.Name);
        }
    }

    private static void ReportBehaviorMethods(SonarCompilationReportingContext context, IEnumerable<IMethodSymbol> methods)
    {
        foreach (var method in methods)
        {
            if (MethodIdentifier(method) is { } identifier)
            {
                context.ReportIssue(CSharpGeneratedCodeRecognizer.Instance, BehaviorFreeMessageRule, identifier.GetLocation(), messageArgs: new[] { method.ContainingType.Name });
            }
        }
    }

    private static SyntaxToken? MethodIdentifier(IMethodSymbol method)
    {
        var declarations = method.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .ToArray();
        return (declarations.FirstOrDefault(x => x.Body is not null || x.ExpressionBody is not null)
                ?? declarations.FirstOrDefault())?.Identifier;
    }

    private static bool TryGetMessageInvocation(SemanticModel model,
                                                InvocationExpressionSyntax invocation,
                                                out INamedTypeSymbol messageType,
                                                out SyntaxNode reportNode,
                                                out MessageContractUse messageUse)
    {
        messageType = null;
        reportNode = invocation;
        messageUse = MessageContractUse.None;
        var use = MessageContractUse.None;

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !MessagingMethods.Contains(method.Name)
            || !GpMessageContracts.IsMessagingMethod(method)
            || (use = ClassifyMessageUse(method)) == MessageContractUse.None)
        {
            return false;
        }

        messageUse = use;
        return TryGetMessageType(model, invocation, method, out messageType, out reportNode);
    }

    private static bool TryGetMessageType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method, out INamedTypeSymbol messageType, out SyntaxNode reportNode)
    {
        if (GpMessageContracts.MessagingPayloadType(model, invocation, MessagingMethods) is not { } payloadType)
        {
            messageType = null;
            reportNode = invocation;
            return false;
        }

        messageType = payloadType;
        if (method.TypeArguments.Length > 0)
        {
            reportNode = invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }
                ? genericName.TypeArgumentList.Arguments.Last()
                : invocation;
            return true;
        }

        if (invocation.ArgumentList.Arguments.FirstOrDefault() is { Expression: var firstArgumentExpression }
            && model.GetTypeInfo(firstArgumentExpression).Type is INamedTypeSymbol argumentType
            && argumentType.Equals(payloadType))
        {
            reportNode = firstArgumentExpression;
            return true;
        }

        reportNode = invocation;
        return true;
    }

    // Compiler-generated members (a record's Equals/ToString/Deconstruct), overrides, explicit interface
    // implementations and value-semantics members are not business behavior - only a method the author added to
    // make the message *do* something is.
    //
    // A private method is not part of the contract a consumer sees: nothing outside the type can call it, so it cannot
    // be mistaken for behavior the message offers across the boundary.
    private static IEnumerable<IMethodSymbol> BusinessBehaviorMethods(Compilation compilation, INamedTypeSymbol messageType) =>
        messageType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(x => x is { MethodKind: MethodKind.Ordinary, IsOverride: false, IsImplicitlyDeclared: false, ExplicitInterfaceImplementations.IsEmpty: true }
                        && x.DeclaredAccessibility != Accessibility.Private
                        && !ValueSemanticsMethods.Contains(x.Name)
                        && !IsFactoryMethod(x, messageType)
                        && !IsDisplayFormattingHelper(compilation, x));

    // A static method handing back an instance of the contract itself constructs the message rather than acting on it.
    private static bool IsFactoryMethod(IMethodSymbol method, INamedTypeSymbol messageType) =>
        method.IsStatic && method.ReturnType.ToDisplayString() == messageType.ToDisplayString();

    private static MessageContractUse ClassifyMessageUse(IMethodSymbol method) =>
        EventMessageMethods.Contains(method.Name)
            ? MessageContractUse.Event
            : CommandMessageMethods.Contains(method.Name)
                ? MessageContractUse.Command
                : MessageContractUse.None;

    // A narrow exception exists for pure display helpers such as AsString(): they format the contract for logs/UI but
    // do not make it perform business work.
    private static bool IsDisplayFormattingHelper(Compilation compilation, IMethodSymbol method)
    {
        if (method is not { Parameters.Length: 0, Arity: 0, IsStatic: false }
            || method.ReturnType.SpecialType != SpecialType.System_String
            || !DisplayFormattingHelperMethods.Contains(method.Name))
        {
            return false;
        }

        var declarations = method.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .ToArray();
        return declarations.Length > 0
               && declarations.All(x => IsDisplayFormattingBody(compilation.GetSemanticModel(x.SyntaxTree), x));
    }

    private static bool IsDisplayFormattingBody(SemanticModel model, MethodDeclarationSyntax declaration) =>
        declaration.ExpressionBody?.Expression is { } expression
            ? IsDisplayFormattingExpression(model, expression)
            : declaration.Body is { Statements.Count: 1 } body
              && body.Statements[0] is ReturnStatementSyntax { Expression: { } returnExpression }
              && IsDisplayFormattingExpression(model, returnExpression);

    private static bool IsDisplayFormattingExpression(SemanticModel model, ExpressionSyntax expression)
    {
        expression = expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
            CastExpressionSyntax cast => cast.Expression,
            _ => expression
        };

        return expression switch
        {
            LiteralExpressionSyntax => true,
            ThisExpressionSyntax => true,
            IdentifierNameSyntax identifier => IsDisplayFormattingReference(model, identifier),
            MemberAccessExpressionSyntax memberAccess => IsDisplayFormattingReference(model, memberAccess.Expression)
                                                        && IsDisplayFormattingReference(model, memberAccess),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } binary
                when model.GetTypeInfo(binary).Type?.SpecialType == SpecialType.System_String =>
                IsDisplayFormattingExpression(model, binary.Left) && IsDisplayFormattingExpression(model, binary.Right),
            InterpolatedStringExpressionSyntax interpolated =>
                interpolated.Contents.All(x => x is InterpolatedStringTextSyntax
                                               || x is InterpolationSyntax interpolation && IsDisplayFormattingExpression(model, interpolation.Expression)),
            InvocationExpressionSyntax invocation => IsDisplayFormattingInvocation(model, invocation),
            _ => false
        };
    }

    private static bool IsDisplayFormattingReference(SemanticModel model, ExpressionSyntax expression) =>
        model.GetSymbolInfo(expression).Symbol is IFieldSymbol
        or IPropertySymbol
        or ILocalSymbol
        or IParameterSymbol
        or INamedTypeSymbol;

    private static bool IsDisplayFormattingInvocation(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return IsKnownStringFormattingMethod(model, invocation, method)
               || IsKnownPrimitiveToString(model, invocation, method);
    }

    private static bool IsKnownStringFormattingMethod(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method is { IsStatic: true, ContainingType.SpecialType: SpecialType.System_String }
        && method.Name is "Concat" or "Format" or "Join"
        && invocation.ArgumentList.Arguments.All(x => IsDisplayFormattingExpression(model, x.Expression));

    private static bool IsKnownPrimitiveToString(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.Name == "ToString"
        && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } instance }
        && IsDisplayFormattingExpression(model, instance)
        && IsDisplayOnlyType(model.GetTypeInfo(instance).Type)
        && invocation.ArgumentList.Arguments.All(x => IsDisplayFormattingExpression(model, x.Expression));

    private static bool IsDisplayOnlyType(ITypeSymbol type)
    {
        type = UnwrapNullable(type);
        return type is not null
               && (type.TypeKind == TypeKind.Enum
                   || type.SpecialType is SpecialType.System_String
                       or SpecialType.System_Boolean
                       or SpecialType.System_Char
                       or SpecialType.System_Decimal
                       or SpecialType.System_Double
                       or SpecialType.System_Single
                       or SpecialType.System_SByte
                       or SpecialType.System_Byte
                       or SpecialType.System_Int16
                       or SpecialType.System_UInt16
                       or SpecialType.System_Int32
                       or SpecialType.System_UInt32
                       or SpecialType.System_Int64
                       or SpecialType.System_UInt64
                   || type.ToDisplayString() is "System.DateTime" or "System.DateTimeOffset" or "System.Guid" or "System.TimeSpan");
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable
            ? nullable.TypeArguments[0]
            : type;
}
