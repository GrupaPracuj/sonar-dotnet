/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractAssemblyShouldNotUseForbiddenTypes : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0059";

    private const string MessageFormat = "'{0}' comes from '{1}', which a contract assembly should not depend on.";

    private const string DefaultContractAssemblyNames = "Contracts";
    private const string DefaultForbiddenNamespaces =
        "Microsoft.EntityFrameworkCore,System.Data.Entity,Microsoft.AspNetCore,MassTransit,RabbitMQ.Client,Consul,Dapper,Microsoft.Extensions.Hosting";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("contractAssemblyNames", PropertyType.String, "Comma-separated names or suffixes identifying contract assemblies", DefaultContractAssemblyNames)]
    public string ContractAssemblyNames { get; set; } = DefaultContractAssemblyNames;

    [RuleParameter("forbiddenNamespaces", PropertyType.String, "Comma-separated namespaces a contract assembly must not use", DefaultForbiddenNamespaces)]
    public string ForbiddenNamespaces { get; set; } = DefaultForbiddenNamespaces;

    // Only worth wiring up at all when the assembly under analysis is a contract assembly, which is known once at
    // compilation start.
    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var forbidden = GpEntityTypes.SplitParameter(ForbiddenNamespaces);
            if (forbidden.Length == 0 || !IsContractAssembly(start.Compilation))
            {
                return;
            }

            start.RegisterNodeAction(
                c => AnalyzeType(c, forbidden),
                SyntaxKind.PropertyDeclaration,
                SyntaxKind.FieldDeclaration,
                SyntaxKind.Parameter,
                SyntaxKind.MethodDeclaration,
                SyntaxKind.DelegateDeclaration,
                SyntaxKind.SimpleBaseType,
                SyntaxKind.TypeConstraint);
        });

    private bool IsContractAssembly(Compilation compilation)
    {
        var names = GpEntityTypes.SplitParameter(ContractAssemblyNames);
        var assemblyName = compilation.AssemblyName ?? string.Empty;
        return names.Length > 0 && Array.Exists(names, x => GpAssemblyNames.Matches(assemblyName, x));
    }

    private static void AnalyzeType(SonarSyntaxNodeReportingContext context, string[] forbidden)
    {
        if (DeclaredType(context.Node) is not { } typeSyntax
            || context.Model.GetTypeInfo(typeSyntax).Type is not { } type
            || ForbiddenType(type, forbidden) is not var (offending, forbiddenNamespace))
        {
            return;
        }

        // Names the offending type, which may be a generic argument rather than the declared type itself.
        context.ReportIssue(Rule, typeSyntax, offending.Name, forbiddenNamespace);
    }

    private static TypeSyntax DeclaredType(SyntaxNode node) =>
        node switch
        {
            PropertyDeclarationSyntax property => property.Type,
            FieldDeclarationSyntax field => field.Declaration.Type,
            ParameterSyntax parameter => parameter.Type,
            MethodDeclarationSyntax method => method.ReturnType,
            DelegateDeclarationSyntax @delegate => @delegate.ReturnType,
            SimpleBaseTypeSyntax baseType => baseType.Type,
            TypeConstraintSyntax constraint => constraint.Type,
            _ => null,
        };

    // Checks the type and its generic arguments, so IReadOnlyList<DbContext> is caught as well as DbContext, and
    // reports which of them is actually the problem.
    private static (ITypeSymbol Type, string Namespace)? ForbiddenType(ITypeSymbol type, string[] forbidden)
    {
        var containing = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (Array.Find(forbidden, x => containing == x || containing.StartsWith(x + ".", StringComparison.Ordinal)) is { } match)
        {
            return (type, match);
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (ForbiddenType(argument, forbidden) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
