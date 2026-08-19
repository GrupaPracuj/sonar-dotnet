/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypeShouldNotBeBothHttpAndMessageContract : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0056";

    private const string MessageFormat = "'{0}' is also an HTTP contract - declare a separate message contract.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> MessagingMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
        "RespondAsync",
        "Publishes",
        "Sends",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    // The set of HTTP contracts is built once from the compilation's symbols, not accumulated from syntax actions
    // as files are visited: node actions run in an arbitrary order across files, so an accumulating set would make
    // the result depend on which file happened to be analyzed first.
    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var httpContracts = HttpContractTypes(start.Compilation);
            if (httpContracts.Count > 0)
            {
                start.RegisterNodeAction(c => ReportMessagingUse(c, httpContracts), SyntaxKind.InvocationExpression);
            }
        });

    private static HashSet<string> HttpContractTypes(Compilation compilation)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var controller in ControllerTypes(compilation.Assembly.GlobalNamespace))
        {
            foreach (var action in controller.GetMembers().OfType<IMethodSymbol>().Where(x => x.IsControllerActionMethod))
            {
                foreach (var exchanged in action.Parameters.Select(x => x.Type).Concat([action.ReturnType]))
                {
                    if (UnwrapType(exchanged) is { } type)
                    {
                        result.Add(type.ToDisplayString());
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<INamedTypeSymbol> ControllerTypes(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers().Where(x => x.IsControllerType))
        {
            yield return type;
        }

        foreach (var nested in root.GetNamespaceMembers())
        {
            foreach (var type in ControllerTypes(nested))
            {
                yield return type;
            }
        }
    }

    private static void ReportMessagingUse(SonarSyntaxNodeReportingContext context, HashSet<string> httpContracts)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !MessagingMethods.Contains(method.Name)
            || !GpMessageContracts.IsMessagingMethod(method)
            || MessageType(context.Model, invocation, method) is not { } messageType
            || !httpContracts.Contains(messageType.ToDisplayString()))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, messageType.Name);
    }

    private static ITypeSymbol MessageType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.TypeArguments.FirstOrDefault()
        ?? (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } firstArgument
            ? model.GetTypeInfo(firstArgument).Type
            : null);

    // Task<T>, ActionResult<T> and collections only wrap the type the endpoint really exchanges.
    private static ITypeSymbol UnwrapType(ITypeSymbol type)
    {
        var current = type;
        while (current is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named)
        {
            current = named.TypeArguments[0];
        }

        return current is { SpecialType: SpecialType.None, TypeKind: TypeKind.Class or TypeKind.Struct } && !IsFrameworkType(current)
            ? current
            : null;
    }

    // An action also takes CancellationToken, Guid and the like. Those are not contracts, and keeping them out of the
    // set stops the rule from ever pairing one with a publish call.
    private static bool IsFrameworkType(ITypeSymbol type) =>
        (type.ContainingNamespace?.ToDisplayString() ?? string.Empty) is var containing
        && (containing == "System"
            || containing.StartsWith("System.", StringComparison.Ordinal)
            || containing.StartsWith("Microsoft.", StringComparison.Ordinal));
}
