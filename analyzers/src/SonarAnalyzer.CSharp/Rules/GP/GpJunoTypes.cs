/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// Shared type tests for the rules that steer callers towards the GP.Juno building blocks. Juno hands out raw
// primitives on purpose (a DbConnection from IAdoConnectionFactory, an IDbConnection into IDbExecute, MassTransit's
// IConsumer<T> for handlers), so these rules key on where a dependency is *obtained*, never on the primitive itself.
internal static class GpJunoTypes
{
    internal const string TransactionalInterface = "GP.Juno.Abstractions.Ado.ITransactional";

    internal static bool DerivesFrom(ITypeSymbol type, string baseTypeDisplayName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == baseTypeDisplayName)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool Implements(ITypeSymbol type, string interfaceDisplayName) =>
        type is not null
        && (type.ToDisplayString() == interfaceDisplayName
            || type.AllInterfaces.Any(x => x.ToDisplayString() == interfaceDisplayName));

    // True when the node sits inside a type that implements the given interface - used to let the type whose job is
    // to produce a Juno primitive call the underlying API the rule otherwise forbids.
    internal static bool IsInsideTypeImplementing(SonarSyntaxNodeReportingContext context, string interfaceDisplayName) =>
        context.Node.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Select(x => context.Model.GetDeclaredSymbol(x))
            .Any(x => Implements(x, interfaceDisplayName));
}
