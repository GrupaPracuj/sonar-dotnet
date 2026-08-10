namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotExposeNonSerializableMembers : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0025";

    private const string MessageFormat = "'{0}' has type '{1}', which does not serialize to JSON meaningfully - remove it from this contract.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Types that either carry no data at all once serialized, or drag a runtime object across a boundary where it
    // means nothing: a process-local handle, a framework service, an ambient request object.
    private static readonly HashSet<string> BannedTypes = new(StringComparer.Ordinal)
    {
        "System.IO.Stream",
        "System.Threading.Tasks.Task",
        "System.IntPtr",
        "System.UIntPtr",
        "System.Exception",
        "System.Type",
        "System.Delegate",
        "System.Threading.CancellationToken",
        "System.IServiceProvider",
        "System.Security.Claims.ClaimsPrincipal",
        "System.Data.DataTable",
        "System.Data.DataSet",
        "Microsoft.AspNetCore.Http.HttpContext",
        "Microsoft.EntityFrameworkCore.DbContext",
        "System.Data.Entity.DbContext",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
        context.RegisterNodeAction(AnalyzeRecordParameters, SyntaxKindEx.RecordDeclaration, SyntaxKindEx.RecordStructDeclaration);
    }

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (GpMessageContracts.IsContractMember(declaration)
            && context.Model.GetDeclaredSymbol(declaration) is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, GetMethod.DeclaredAccessibility: Accessibility.Public } property
            && !IsIgnored(property)
            && BannedType(context.Model, declaration.Type) is { } typeName)
        {
            context.ReportIssue(Rule, declaration, declaration.Identifier.ValueText, typeName);
        }
    }

    // A positional parameter of a record - class or struct - is a public member of the serialized instance just as much as a property is.
    private static void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier.ValueText: var typeName } declaration
            || !GpMessageContracts.HasContractName(typeName)
            || !RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            || ((RecordDeclarationSyntaxWrapper)declaration).ParameterList is not { } parameterList)
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters.Where(x => x.Type is not null))
        {
            if (context.Model.GetDeclaredSymbol(declaration) is { } recordType
                && recordType.GetMembers(parameter.Identifier.ValueText).OfType<IPropertySymbol>().FirstOrDefault() is { } property
                && !IsIgnored(property)
                && BannedType(context.Model, parameter.Type) is { } bannedType)
            {
                context.ReportIssue(Rule, parameter, parameter.Identifier.ValueText, bannedType);
            }
        }
    }

    private static void AnalyzeField(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (FieldDeclarationSyntax)context.Node;
        if (!GpMessageContracts.IsContractMember(declaration)
            || declaration.Modifiers.All(x => !x.IsKind(SyntaxKind.PublicKeyword))
            // A static or const field is not part of the serialized instance, so it is not part of the contract.
            || declaration.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword) || x.IsKind(SyntaxKind.ConstKeyword))
            || BannedType(context.Model, declaration.Declaration.Type) is not { } typeName)
        {
            return;
        }

        foreach (var variable in declaration.Declaration.Variables)
        {
            if (context.Model.GetDeclaredSymbol(variable) is { } field && !IsIgnored(field))
            {
                context.ReportIssue(Rule, variable, variable.Identifier.ValueText, typeName);
            }
        }
    }

    private static bool IsIgnored(ISymbol symbol) =>
        symbol.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() is
            "System.Text.Json.Serialization.JsonIgnoreAttribute" or "Newtonsoft.Json.JsonIgnoreAttribute");

    private static string BannedType(SemanticModel model, TypeSyntax typeSyntax) =>
        model.GetTypeInfo(typeSyntax).Type is { } type && IsBannedType(type)
            ? type.ToDisplayString()
            : null;

    private static bool IsBannedType(ITypeSymbol type) =>
        BannedTypes.Contains(type.ToDisplayString())
        || DerivesFromBannedType(type)
        || (type is INamedTypeSymbol { IsGenericType: true, Name: "Task" } named && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
        // Any delegate, not only Action/Func: a custom delegate is just as much a method reference.
        || type.TypeKind == TypeKind.Delegate;

    // Exception and DbContext are almost always used through a derived type, so the base classes have to be walked.
    private static bool DerivesFromBannedType(ITypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (BannedTypes.Contains(current.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }
}
