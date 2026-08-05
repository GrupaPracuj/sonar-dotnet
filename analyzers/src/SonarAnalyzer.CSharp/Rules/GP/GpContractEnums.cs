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
                if (EnumType(member.Type) is { } enumType)
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
    private static INamedTypeSymbol EnumType(ITypeSymbol type)
    {
        var candidate = type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic => generic.TypeArguments[0],
            _ => type,
        };

        return candidate is INamedTypeSymbol { TypeKind: TypeKind.Enum } named ? named : null;
    }

    private static IEnumerable<INamedTypeSymbol> ContractTypes(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers().Where(x => GpMessageContracts.HasContractName(x.Name)))
        {
            yield return type;
        }

        foreach (var nested in root.GetNamespaceMembers())
        {
            foreach (var type in ContractTypes(nested))
            {
                yield return type;
            }
        }
    }
}
