/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// The enums that contracts actually expose on the wire.
//
// The set is built once per compilation from symbols, not accumulated as files are visited, so the result does not
// depend on which file the analyzer happens to reach first. That also lets the enum rules report on the enum
// declaration itself - the place to fix - while still only reporting enums a contract exposes.
internal sealed class GpContractEnums
{
    private readonly HashSet<string> enumsUsedByContracts;

    private GpContractEnums(HashSet<string> enumsUsedByContracts) =>
        this.enumsUsedByContracts = enumsUsedByContracts;

    internal bool IsEmpty => enumsUsedByContracts.Count == 0;

    internal static GpContractEnums Create(GpSemanticContractDetector contracts) =>
        Create(contracts.SourceContracts);

    private static GpContractEnums Create(IEnumerable<INamedTypeSymbol> contractTypes)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in contractTypes)
        {
            foreach (var member in GpMessageContracts.DataMembers(contract))
            {
                foreach (var enumType in EnumTypes(member.Type))
                {
                    result.Add(enumType.ToDisplayString());
                }
            }
        }

        return new GpContractEnums(result);
    }

    internal bool IsUsedByAContract(INamedTypeSymbol enumType) =>
        enumsUsedByContracts.Contains(enumType.ToDisplayString());

    // Nullable and collection wrappers still put the enum on the wire.
    private static IEnumerable<INamedTypeSymbol> EnumTypes(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            yield return enumType;
            yield break;
        }

        if (type is IArrayTypeSymbol array)
        {
            foreach (var nested in EnumTypes(array.ElementType))
            {
                yield return nested;
            }
        }
        else if (type is INamedTypeSymbol { IsGenericType: true } generic
                 && (generic.OriginalDefinition.Is(KnownType.System_Nullable_T) || GpCollectionEndpointHelper.IsCollectionLike(generic)))
        {
            foreach (var argument in generic.TypeArguments)
            {
                foreach (var nested in EnumTypes(argument))
                {
                    yield return nested;
                }
            }
        }
    }

}
