namespace SonarAnalyzer.CSharp.Rules;

// Recognises "this type belongs to the database or the domain, not to a wire contract".
//
// Two of the four signals are framework-based and work with no configuration: appearing as the element type of a
// DbSet<T> anywhere in the compilation, and carrying EF mapping attributes. The other two encode a per-solution
// convention (base types, namespaces) and are therefore driven by rule parameters rather than hardcoded.
//
// The DbSet scan walks every type in the compilation's own assembly plus every non-framework referenced assembly
// (a DbContext frequently lives in a separate persistence project), so it is done once per compilation through
// Create and the result reused, rather than repeated at each call site being analyzed.
internal sealed class GpEntityTypes
{
    private static readonly HashSet<string> EntityAttributes = new(StringComparer.Ordinal)
    {
        "TableAttribute",
        "KeyAttribute",
        "ColumnAttribute",
        "ForeignKeyAttribute",
        "DatabaseGeneratedAttribute",
        "PrimaryKeyAttribute",
    };

    private readonly HashSet<string> dbSetElementTypes;
    private readonly string[] entityBaseTypes;
    private readonly string[] domainNamespaces;

    private GpEntityTypes(HashSet<string> dbSetElementTypes, string[] entityBaseTypes, string[] domainNamespaces)
    {
        this.dbSetElementTypes = dbSetElementTypes;
        this.entityBaseTypes = entityBaseTypes;
        this.domainNamespaces = domainNamespaces;
    }

    internal static GpEntityTypes Create(Compilation compilation, string entityBaseTypes, string domainNamespaces) =>
        new(DbSetElementTypes(compilation), SplitParameter(entityBaseTypes), SplitParameter(domainNamespaces));

    internal bool IsEntity(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && (HasEntityAttribute(named)
            || DerivesFromConfiguredBase(named)
            || IsInConfiguredNamespace(named)
            || dbSetElementTypes.Contains(named.OriginalDefinition.ToDisplayString()));

    internal static string[] SplitParameter(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

    private static bool HasEntityAttribute(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IPropertySymbol>().SelectMany(x => x.GetAttributes()).Concat(type.GetAttributes())
            .Any(x => x.AttributeClass?.Name is { } name && EntityAttributes.Contains(name));

    private bool DerivesFromConfiguredBase(INamedTypeSymbol type)
    {
        if (entityBaseTypes.Length == 0)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (Array.Exists(entityBaseTypes, x => current.Name == x || current.ToDisplayString() == x))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInConfiguredNamespace(INamedTypeSymbol type)
    {
        var containing = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return Array.Exists(domainNamespaces, x => containing == x || containing.StartsWith(x + ".", StringComparison.Ordinal));
    }

    // A type mapped by EF is reachable as DbSet<T> on some DbContext in the compilation. Looking at the contexts
    // rather than the type itself catches entities configured purely through Fluent API, which carry no attributes.
    //
    // The DbContext commonly lives in a referenced persistence project rather than in the assembly being analyzed
    // (a typical layered solution has the API/contract assembly reference a separate data-access assembly), so every
    // referenced assembly is scanned as well - except framework assemblies (BCL, "System.*", "Microsoft.*"), which
    // are numerous, large, and never declare a solution's own DbContext, so walking their types would be wasted work.
    private static HashSet<string> DbSetElementTypes(Compilation compilation)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in RelevantAssemblies(compilation))
        {
            foreach (var context in DbContextTypes(assembly.GlobalNamespace))
            {
                foreach (var property in context.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.Type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } dbSet
                        && dbSet.ConstructedFrom.Is(KnownType.Microsoft_EntityFrameworkCore_DbSet_TEntity))
                    {
                        result.Add(dbSet.TypeArguments[0].OriginalDefinition.ToDisplayString());
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<IAssemblySymbol> RelevantAssemblies(Compilation compilation)
    {
        yield return compilation.Assembly;

        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!IsFrameworkAssembly(reference))
            {
                yield return reference;
            }
        }
    }

    private static bool IsFrameworkAssembly(IAssemblySymbol assembly) =>
        assembly.Name is "mscorlib" or "netstandard" or "WindowsBase"
        || assembly.Name.StartsWith("System", StringComparison.Ordinal)
        || assembly.Name.StartsWith("Microsoft.", StringComparison.Ordinal);

    private static IEnumerable<INamedTypeSymbol> DbContextTypes(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            if (GpJunoTypes.DerivesFrom(type, "Microsoft.EntityFrameworkCore.DbContext")
                || GpJunoTypes.DerivesFrom(type, "System.Data.Entity.DbContext"))
            {
                yield return type;
            }
        }

        foreach (var nested in root.GetNamespaceMembers())
        {
            foreach (var type in DbContextTypes(nested))
            {
                yield return type;
            }
        }
    }
}
