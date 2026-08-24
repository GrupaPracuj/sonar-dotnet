/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using System.Text.RegularExpressions;

namespace SonarAnalyzer.CSharp.Rules;

internal enum GpQueryBound
{
    Unknown,
    Bounded,
    Unbounded,
}

internal static class GpDatabaseCallHelper
{
    private const string DapperSqlMapper = "Dapper.SqlMapper";
    private const string EfQueryableExtensions = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string JunoDbExecuteNamespace = "GP.Juno.Abstractions.Ado";

    private static readonly HashSet<string> EfExecutionMethods = new(StringComparer.Ordinal)
    {
        "All",
        "Any",
        "Average",
        "Contains",
        "Count",
        "First",
        "ForEach",
        "LongCount",
        "Max",
        "Min",
        "Single",
        "Sum",
        "ToArray",
        "ToDictionary",
        "ToHashSet",
        "ToList",
    };

    private static readonly HashSet<string> KnownEfCompositionMethods = new(StringComparer.Ordinal)
    {
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "AsSplitQuery",
        "AsTracking",
        "Cast",
        "Distinct",
        "GroupBy",
        "IgnoreAutoIncludes",
        "IgnoreQueryFilters",
        "Include",
        "OfType",
        "OrderBy",
        "OrderByDescending",
        "Select",
        "SelectMany",
        "Skip",
        "TagWith",
        "ThenBy",
        "ThenByDescending",
        "Where",
    };

    private static readonly Regex SqlLimit = new(
        @"\bTOP\s*(?:\(\s*)?(?:@\w+|\d+)|\bLIMIT\s+(?:@\w+|\d+)|\bFETCH\s+(?:FIRST|NEXT)\s+(?:@\w+|\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SingleAggregate = new(
        @"^\s*SELECT\s+(?:DISTINCT\s+)?(?:COUNT|SUM|AVG|MIN|MAX)\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool IsDatabaseCall(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        IsDapper(method)
        || (IsEfExecution(method)
            && EfQueryBound(model, invocation, method) != GpQueryBound.Unknown)
        || IsJunoExecute(model, invocation, method);

    internal static bool IsDapperCollectionQuery(IMethodSymbol method) =>
        IsDapper(method) && method.Name is "Query" or "QueryAsync" or "QueryUnbufferedAsync";

    internal static bool TryGetDapperSql(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        out string sql)
    {
        sql = null;
        var mappings = new CSharpMethodParameterLookup(invocation, method).GetAllArgumentParameterMappings().ToArray();
        if (HasNonTextCommandType(model, mappings))
        {
            return false;
        }

        var expression = mappings
            .Where(x => x.Symbol.Name is "sql" or "commandText")
            .Select(x => x.Node?.Expression)
            .FirstOrDefault(x => x is not null);
        if (expression is null)
        {
            var command = mappings
                .Where(x => x.Symbol.Type.Is(KnownType.Dapper_CommandDefinition))
                .Select(x => x.Node?.Expression)
                .FirstOrDefault(x => x is not null);
            if (CommandCreation(model, command) is not { ArgumentList: { } arguments } creation
                || creation.MethodSymbol(model) is not { } constructor)
            {
                return false;
            }

            var constructorMappings = new CSharpMethodParameterLookup(arguments, constructor).GetAllArgumentParameterMappings().ToArray();
            if (HasNonTextCommandType(model, constructorMappings))
            {
                return false;
            }
            expression = constructorMappings
                .Where(x => x.Symbol.Name is "commandText" or "sql")
                .Select(x => x.Node?.Expression)
                .FirstOrDefault(x => x is not null);
        }

        if (expression is not null
            && model.GetConstantValue(expression) is { HasValue: true, Value: string constantSql })
        {
            sql = constantSql;
            return true;
        }
        return false;
    }

    internal static bool IsResultSetBounded(string sql) =>
        SqlLimit.IsMatch(sql)
        || (SingleAggregate.IsMatch(sql) && sql.IndexOf("GROUP BY", StringComparison.OrdinalIgnoreCase) < 0);

    internal static bool IsEfCollectionMaterializer(IMethodSymbol method)
    {
        var name = RemoveAsyncSuffix(method.Name);
        return IsEfMethod(method) && name is "ToArray" or "ToDictionary" or "ToHashSet" or "ToList";
    }

    internal static GpQueryBound EfQueryBound(
        SemanticModel model,
        InvocationExpressionSyntax materializer,
        IMethodSymbol method) =>
        QuerySource(materializer, method) is { } source
            ? EfQueryBound(model, source, new HashSet<ISymbol>())
            : GpQueryBound.Unknown;

    private static GpQueryBound EfQueryBound(SemanticModel model, ExpressionSyntax expression, HashSet<ISymbol> visited)
    {
        expression = expression.RemoveParentheses() as ExpressionSyntax ?? expression;
        if (expression is InvocationExpressionSyntax invocation
            && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
        {
            var name = RemoveAsyncSuffix(method.Name);
            if (name == "Take" && IsLinqOrEfMethod(method))
            {
                return GpQueryBound.Bounded;
            }
            return KnownEfCompositionMethods.Contains(name) && IsLinqOrEfMethod(method) && QuerySource(invocation, method) is { } source
                ? EfQueryBound(model, source, visited)
                : GpQueryBound.Unknown;
        }

        if (model.GetSymbolInfo(expression).Symbol is ILocalSymbol local && visited.Add(local))
        {
            return local.DeclaringSyntaxReferences
                .Select(x => x.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .Select(x => x.Initializer?.Value)
                .WhereNotNull()
                .Select(x => EfQueryBound(model, x, visited))
                .DefaultIfEmpty(GpQueryBound.Unknown)
                .Aggregate(Merge);
        }

        return IsDbSet(model.GetTypeInfo(expression).Type)
            ? GpQueryBound.Unbounded
            : GpQueryBound.Unknown;
    }

    private static GpQueryBound Merge(GpQueryBound left, GpQueryBound right) =>
        left == right ? left : GpQueryBound.Unknown;

    private static ExpressionSyntax QuerySource(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method.ReducedFrom is not null
            && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } receiver })
        {
            return receiver;
        }

        return new CSharpMethodParameterLookup(invocation, method).GetAllArgumentParameterMappings()
            .Where(x => x.Symbol.Name is "source")
            .Select(x => x.Node?.Expression)
            .FirstOrDefault(x => x is not null);
    }

    private static bool IsDapper(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() == DapperSqlMapper
        && (method.Name.StartsWith("Query", StringComparison.Ordinal)
            || method.Name.StartsWith("Execute", StringComparison.Ordinal));

    private static bool IsEfExecution(IMethodSymbol method) =>
        IsEfMethod(method) && EfExecutionMethods.Contains(RemoveAsyncSuffix(method.Name));

    private static bool IsEfMethod(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() is
            EfQueryableExtensions or "System.Linq.Queryable" or "System.Linq.Enumerable";

    private static bool IsLinqOrEfMethod(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() is
            EfQueryableExtensions or "System.Linq.Queryable";

    private static bool IsJunoExecute(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.Name == "Execute"
        && invocation.ArgumentList.Arguments
            .Select(x => model.GetTypeInfo(x.Expression).Type)
            .Any(IsJunoDbExecute);

    private static bool IsJunoDbExecute(ITypeSymbol type) =>
        type is not null
        && type.AllInterfaces.Prepend(type)
            .Any(x => x.Name == "IDbExecute" && x.ContainingNamespace?.ToDisplayString() == JunoDbExecuteNamespace);

    private static bool HasNonTextCommandType(
        SemanticModel model,
        IEnumerable<NodeAndSymbol<ArgumentSyntax, IParameterSymbol>> mappings) =>
        mappings.Where(x => x.Symbol.Name == "commandType" && x.Node?.Expression is not null)
            .Select(x => model.GetConstantValue(x.Node.Expression))
            .Any(x => x is not { HasValue: true, Value: null } and not { HasValue: true, Value: 1 });

    private static IObjectCreation CommandCreation(SemanticModel model, ExpressionSyntax expression)
    {
        if (expression is null)
        {
            return null;
        }
        if (ObjectCreationFactory.TryCreate(expression) is { } creation)
        {
            return creation;
        }
        return model.GetSymbolInfo(expression).Symbol is ILocalSymbol local
            ? local.DeclaringSyntaxReferences
                .Select(x => x.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .Select(x => x.Initializer?.Value)
                .Select(ObjectCreationFactory.TryCreate)
                .WhereNotNull()
                .SingleOrDefault()
            : null;
    }

    private static bool IsDbSet(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.ToDisplayString() == "Microsoft.EntityFrameworkCore.DbSet<TEntity>";

    private static string RemoveAsyncSuffix(string name) =>
        name.EndsWith("Async", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Async".Length)
            : name;
}
