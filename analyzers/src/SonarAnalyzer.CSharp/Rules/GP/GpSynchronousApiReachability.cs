/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using System.Runtime.CompilerServices;

namespace SonarAnalyzer.CSharp.Rules;

internal static class GpSynchronousApiReachability
{
    private static readonly HashSet<string> MinimalApiMapMethods = new(StringComparer.Ordinal)
    {
        "Map",
        "MapDelete",
        "MapGet",
        "MapMethods",
        "MapPatch",
        "MapPost",
        "MapPut",
    };

    private static readonly ConditionalWeakTable<Compilation, Lazy<Reachability>> Cache = new();

    internal static bool IsReachable(SemanticModel model, SyntaxNode node) =>
        model.GetEnclosingSymbol(node.SpanStart) is IMethodSymbol method
        && Cache.GetValue(
            model.Compilation,
            compilation => new Lazy<Reachability>(
                () => Build(compilation),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value.Contains(method);

    private static Reachability Build(Compilation compilation)
    {
        var roots = new HashSet<IMethodSymbol>();
        var calls = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>();
        var declaredMethods = new List<IMethodSymbol>();

        foreach (var tree in compilation.SyntaxTrees.Where(x => !x.IsGenerated(CSharpGeneratedCodeRecognizer.Instance)))
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not { } method)
                {
                    continue;
                }

                method = Normalize(method);
                declaredMethods.Add(method);
                if (GpOpenApiMetadata.IsOpenApiAction(method) && !GpOpenApiMetadata.IsIgnored(method))
                {
                    roots.Add(method);
                }
            }

            foreach (var lambda in root.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
            {
                var nodeInHandler = lambda switch
                {
                    SimpleLambdaExpressionSyntax simple => simple.Body,
                    ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.Body,
                    AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
                    _ => null,
                };
                if (nodeInHandler is not null
                    && GpMinimalApi.TryGetInlineHandler(
                        nodeInHandler,
                        model,
                        MinimalApiMapMethods,
                        out _,
                        out _,
                        out _,
                        out _)
                    && model.GetSymbolInfo(lambda).Symbol is IMethodSymbol handler)
                {
                    roots.Add(Normalize(handler));
                }
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetEnclosingSymbol(invocation.SpanStart) is IMethodSymbol caller
                    && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol target)
                {
                    AddEdge(calls, Normalize(caller), Normalize(target));
                }
            }
        }

        AddDispatchEdges(declaredMethods, calls);

        var reachable = new HashSet<IMethodSymbol>(roots);
        var pending = new Queue<IMethodSymbol>(roots);
        while (pending.Count > 0)
        {
            if (!calls.TryGetValue(pending.Dequeue(), out var targets))
            {
                continue;
            }

            foreach (var target in targets.Where(reachable.Add))
            {
                pending.Enqueue(target);
            }
        }

        return new Reachability(reachable);
    }

    private static void AddDispatchEdges(
        IEnumerable<IMethodSymbol> methods,
        Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> calls)
    {
        foreach (var implementation in methods)
        {
            if (implementation.OverriddenMethod is { } overridden)
            {
                AddEdge(calls, Normalize(overridden), implementation);
            }

            foreach (var explicitImplementation in implementation.ExplicitInterfaceImplementations)
            {
                AddEdge(calls, Normalize(explicitImplementation), implementation);
            }

            foreach (var interfaceType in implementation.ContainingType.AllInterfaces)
            {
                foreach (var member in interfaceType.GetMembers(implementation.Name).OfType<IMethodSymbol>())
                {
                    if (implementation.ContainingType.FindImplementationForInterfaceMember(member) is IMethodSymbol found
                        && Equals(Normalize(found), implementation))
                    {
                        AddEdge(calls, Normalize(member), implementation);
                    }
                }
            }
        }
    }

    private static void AddEdge(
        Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> calls,
        IMethodSymbol caller,
        IMethodSymbol target)
    {
        if (!calls.TryGetValue(caller, out var targets))
        {
            targets = new HashSet<IMethodSymbol>();
            calls.Add(caller, targets);
        }
        targets.Add(target);
    }

    private static IMethodSymbol Normalize(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).OriginalDefinition;

    private sealed class Reachability(HashSet<IMethodSymbol> methods)
    {
        internal bool Contains(IMethodSymbol method) => methods.Contains(Normalize(method));
    }
}
