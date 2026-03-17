using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG006: Non-deterministic (non-collection) value in RegisterSourceOutput/RegisterImplementationSourceOutput.
/// LSG007: Non-deterministic collection value in RegisterSourceOutput/RegisterImplementationSourceOutput.
/// LSG008: Non-deterministic SyntaxProvider return value (warning).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeterministicValueAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> RegisterMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "RegisterSourceOutput",
        "RegisterImplementationSourceOutput");

    private static readonly ImmutableHashSet<string> SyntaxProviderMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "CreateSyntaxProvider",
        "ForAttributeWithMetadataName");

    // Non-deterministic types that should not be passed to RegisterSourceOutput
    private static readonly ImmutableHashSet<string> NonDeterministicTypeNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ISymbol",
        "SyntaxNode",
        "SemanticModel",
        "Compilation",
        "INamedTypeSymbol",
        "IMethodSymbol",
        "IPropertySymbol",
        "IFieldSymbol",
        "IEventSymbol",
        "IParameterSymbol",
        "ITypeSymbol",
        "ITypeParameterSymbol",
        "IAssemblySymbol",
        "IModuleSymbol",
        "INamespaceSymbol",
        "ILocalSymbol",
        "ILabelSymbol",
        "IRangeVariableSymbol",
        "IDiscardSymbol",
        "IFunctionPointerTypeSymbol",
        "IPointerTypeSymbol",
        "IDynamicTypeSymbol",
        "IArrayTypeSymbol",
        "CompilationUnitSyntax",
        "ClassDeclarationSyntax",
        "MethodDeclarationSyntax",
        "PropertyDeclarationSyntax",
        "AttributeSyntax"
    );

    // Collection types that use reference equality
    private static readonly ImmutableHashSet<string> CollectionTypeNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "List",
        "ImmutableArray",
        "IList",
        "IReadOnlyList",
        "ICollection",
        "IReadOnlyCollection",
        "IEnumerable",
        "Array");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.LSG006,
            DiagnosticDescriptors.LSG007,
            DiagnosticDescriptors.LSG008);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;

        if (RegisterMethods.Contains(methodName))
        {
            AnalyzeRegisterSourceOutput(context, invocation);
        }
        else if (SyntaxProviderMethods.Contains(methodName))
        {
            AnalyzeSyntaxProviderReturn(context, invocation);
        }
    }

    private static void AnalyzeRegisterSourceOutput(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 1)
            return;

        var sourceArg = args[0];
        var typeInfo = context.SemanticModel.GetTypeInfo(sourceArg.Expression);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;

        if (type == null)
            return;

        CheckTypeForNonDeterminism(context, type, invocation.GetLocation(), isRegisterMethod: true,
            ImmutableHashSet<string>.Empty);
    }

    private static void AnalyzeSyntaxProviderReturn(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(invocation);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;

        if (type == null)
            return;

        if (type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            (namedType.Name == "IncrementalValueProvider" || namedType.Name == "IncrementalValuesProvider"))
        {
            if (namedType.TypeArguments.Length > 0)
            {
                CheckTypeForNonDeterminism(context, namedType.TypeArguments[0],
                    invocation.GetLocation(), isRegisterMethod: false,
                    ImmutableHashSet<string>.Empty);
            }
        }
    }

    private static void CheckTypeForNonDeterminism(
        SyntaxNodeAnalysisContext context,
        ITypeSymbol type,
        Location location,
        bool isRegisterMethod,
        ImmutableHashSet<string> visitedTypeIds)
    {
        // Unwrap IncrementalValueProvider<T> / IncrementalValuesProvider<T>
        if (type is INamedTypeSymbol namedTypeOuter &&
            namedTypeOuter.IsGenericType &&
            (namedTypeOuter.Name == "IncrementalValueProvider" || namedTypeOuter.Name == "IncrementalValuesProvider"))
        {
            if (namedTypeOuter.TypeArguments.Length > 0)
                type = namedTypeOuter.TypeArguments[0];
        }

        var typeName = type.Name;
        var fullTypeName = type.ToDisplayString();

        // Arrays -> LSG007 / LSG008
        if (type is IArrayTypeSymbol)
        {
            ReportDiagnostic(context, location, isRegisterMethod,
                DiagnosticDescriptors.LSG007, DiagnosticDescriptors.LSG008, fullTypeName);
            return;
        }

        // Well-known collection types -> LSG007 / LSG008
        if (type is INamedTypeSymbol collType && collType.IsGenericType &&
            CollectionTypeNames.Contains(collType.Name))
        {
            ReportDiagnostic(context, location, isRegisterMethod,
                DiagnosticDescriptors.LSG007, DiagnosticDescriptors.LSG008, fullTypeName);
            return;
        }

        // Well-known non-deterministic type names -> LSG006 / LSG008
        if (NonDeterministicTypeNames.Contains(typeName))
        {
            ReportDiagnostic(context, location, isRegisterMethod,
                DiagnosticDescriptors.LSG006, DiagnosticDescriptors.LSG008, fullTypeName);
            return;
        }

        if (type is not INamedTypeSymbol checkType)
            return;

        // Implements ISymbol or derives from SyntaxNode/SemanticModel/Compilation -> LSG006 / LSG008
        if (ImplementsInterface(checkType, "ISymbol", "Microsoft.CodeAnalysis") ||
            DerivesFrom(checkType, "SyntaxNode", "Microsoft.CodeAnalysis") ||
            DerivesFrom(checkType, "SemanticModel", "Microsoft.CodeAnalysis") ||
            DerivesFrom(checkType, "Compilation", "Microsoft.CodeAnalysis"))
        {
            ReportDiagnostic(context, location, isRegisterMethod,
                DiagnosticDescriptors.LSG006, DiagnosticDescriptors.LSG008, fullTypeName);
            return;
        }

        // User-defined mutable class (not record/struct) -> LSG006 / LSG008
        if (IsUserDefinedMutableClass(checkType))
        {
            ReportDiagnostic(context, location, isRegisterMethod,
                DiagnosticDescriptors.LSG006, DiagnosticDescriptors.LSG008, fullTypeName);
            return;
        }

        // Prevent infinite recursion for self-referential types
        var typeId = checkType.ToDisplayString();
        if (visitedTypeIds.Contains(typeId))
            return;
        var updatedVisited = visitedTypeIds.Add(typeId);

        // Tuples: check each element type recursively
        if (checkType.IsTupleType)
        {
            foreach (var element in checkType.TupleElements)
                CheckTypeForNonDeterminism(context, element.Type, location, isRegisterMethod, updatedVisited);
            return;
        }

        // Generic type arguments (ValueTuple<T1,T2>, wrapper records, etc.)
        if (checkType.IsGenericType)
        {
            foreach (var typeArg in checkType.TypeArguments)
                CheckTypeForNonDeterminism(context, typeArg, location, isRegisterMethod, updatedVisited);
        }

        // Records and structs: recursively check all property/field types
        if (checkType.IsRecord || checkType.TypeKind == TypeKind.Struct)
        {
            foreach (var member in checkType.GetMembers())
            {
                // Skip compiler-generated backing fields and other implicit members
                if (member.IsImplicitlyDeclared)
                    continue;

                ITypeSymbol? memberType = member switch
                {
                    IPropertySymbol prop when !prop.IsStatic => prop.Type,
                    IFieldSymbol field when !field.IsStatic => field.Type,
                    _ => null
                };
                if (memberType != null)
                    CheckTypeForNonDeterminism(context, memberType, location, isRegisterMethod, updatedVisited);
            }
        }
    }

    private static void ReportDiagnostic(
        SyntaxNodeAnalysisContext context,
        Location location,
        bool isRegisterMethod,
        DiagnosticDescriptor registerDescriptor,
        DiagnosticDescriptor syntaxProviderDescriptor,
        string fullTypeName)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            isRegisterMethod ? registerDescriptor : syntaxProviderDescriptor,
            location,
            fullTypeName));
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string interfaceName, string namespaceName)
    {
        return type.AllInterfaces.Any(i =>
            i.Name == interfaceName &&
            i.ContainingNamespace?.ToString() == namespaceName);
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string baseTypeName, string namespaceName)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.Name == baseTypeName &&
                current.ContainingNamespace?.ToString() == namespaceName)
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool IsUserDefinedMutableClass(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class)
            return false;
        if (type.IsRecord)
            return false;
        var ns = type.ContainingNamespace?.ToString();
        if (ns != null && (ns.StartsWith("System", StringComparison.Ordinal) ||
                           ns.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)))
            return false;
        if (type.IsAnonymousType)
            return false;
        return true;
    }
}
