/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControllersShouldNotUseInfrastructureDirectly : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0032";

    private const string MessageFormat = "Do not use infrastructure type '{0}' directly in {1} - depend on an application abstraction instead.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);

    private static void AnalyzeClass(SonarSyntaxNodeReportingContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(classDeclaration) is not { } type)
        {
            return;
        }

        var isController = type.IsControllerType;
        var isApiProject = IsApiProject(context.Compilation);
        if (!isController && !isApiProject)
        {
            return;
        }

        // Where a persistence operation is declared is a placement question, left to the projects themselves; this rule
        // is about transport code using one.
        foreach (var declaredType in DeclaredTypes(classDeclaration))
        {
            if (InfrastructureTypeName(context.Model, declaredType, isController) is { } name)
            {
                context.ReportIssue(Rule, declaredType, name, isController ? "a controller" : "an API project");
            }
        }
    }

    // The three ways infrastructure reaches a controller: injected into a field, taken as a constructor parameter,
    // or created as a local. Action parameters are not included because model binding is a separate concern.
    private static IEnumerable<TypeSyntax> DeclaredTypes(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var parameter in classDeclaration.ParameterList()?.Parameters ?? [])
        {
            if (parameter.Type is { } parameterType)
            {
                yield return parameterType;
            }
        }

        foreach (var field in classDeclaration.Members.OfType<FieldDeclarationSyntax>())
        {
            yield return field.Declaration.Type;
        }

        foreach (var parameter in classDeclaration.Members.OfType<ConstructorDeclarationSyntax>().SelectMany(x => x.ParameterList.Parameters))
        {
            if (parameter.Type is { } parameterType)
            {
                yield return parameterType;
            }
        }

        foreach (var local in classDeclaration.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            yield return local.Declaration.Type;
        }
    }

    private static string InfrastructureTypeName(SemanticModel model, TypeSyntax typeSyntax, bool isController) =>
        model.GetTypeInfo(typeSyntax).Type is { } type
        && (isController ? IsControllerInfrastructure(type) : IsApiPersistenceDependency(type))
            ? type.Name
            : null;

    private static bool IsControllerInfrastructure(ITypeSymbol type) =>
        IsApiPersistenceDependency(type)
        || IsPersistenceOperation(type)
        || GpJunoTypes.DerivesFrom(type, "System.Net.Http.HttpClient");

    private static bool IsApiPersistenceDependency(ITypeSymbol type) =>
        GpJunoTypes.DerivesFrom(type, "Microsoft.EntityFrameworkCore.DbContext")
        || GpJunoTypes.DerivesFrom(type, "System.Data.Entity.DbContext")
        || GpJunoTypes.DerivesFrom(type, "System.Data.Common.DbConnection");

    internal static bool IsApiProject(Compilation compilation) =>
        compilation.AssemblyName?.EndsWith(".Api", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsPersistenceOperation(ITypeSymbol type) =>
        GpJunoTypes.Implements(type, GpJunoTypes.TransactionalInterface)
        || IsDbExecute(type);

    internal static bool IsDbExecute(ITypeSymbol type) =>
        type.AllInterfaces.Any(x =>
            x.Name == "IDbExecute"
            && x.ContainingNamespace.ToDisplayString() == "GP.Juno.Abstractions.Ado");
}
