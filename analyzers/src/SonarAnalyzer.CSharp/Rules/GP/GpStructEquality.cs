namespace SonarAnalyzer.CSharp.Rules;

// Shared by DefaultStructEqualityShouldNotBeUsed (GP0085): a struct that has not overridden Equals(object) - and
// correspondingly GetHashCode() - falls back to System.ValueType.Equals, which uses reflection to compare every
// field, and compares reference-type fields by reference rather than by value.
internal static class GpStructEquality
{
    // Record structs are exempt: the compiler synthesizes correct, fast, field-based equality members for them.
    // Enums are a different TypeKind entirely and are not a concern here.
    internal static bool UsesDefaultEquality(ITypeSymbol type) =>
        type is { TypeKind: TypeKind.Struct }
        && !type.IsRecord()
        && (UsesDefaultEquals(type) || UsesDefaultGetHashCode(type));

    internal static bool UsesDefaultEquals(ITypeSymbol type) =>
        !type.GetMembers(nameof(Equals)).OfType<IMethodSymbol>().Any(x => x.IsOverride && x.Parameters.Length == 1);

    private static bool UsesDefaultGetHashCode(ITypeSymbol type) =>
        !type.GetMembers(nameof(GetHashCode)).OfType<IMethodSymbol>().Any(x => x.IsOverride && x.Parameters.Length == 0);
}
