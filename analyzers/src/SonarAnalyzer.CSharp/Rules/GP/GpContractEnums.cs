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

    internal static GpContractEnums Create(Compilation compilation)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in ContractTypes(compilation.Assembly.GlobalNamespace))
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

    private static IEnumerable<INamedTypeSymbol> ContractTypes(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            if (GpMessageContracts.HasContractName(type.Name))
            {
                yield return type;
            }

            foreach (var nestedType in ContractTypes(type))
            {
                yield return nestedType;
            }
        }

        foreach (var nestedNamespace in root.GetNamespaceMembers())
        {
            foreach (var type in ContractTypes(nestedNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> ContractTypes(INamedTypeSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            if (GpMessageContracts.HasContractName(type.Name))
            {
                yield return type;
            }

            foreach (var nested in ContractTypes(type))
            {
                yield return nested;
            }
        }
    }
}
