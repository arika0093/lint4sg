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
            AnalyzeRegisterSourceOutput(context, invocation, methodName);
        }
        else if (SyntaxProviderMethods.Contains(methodName))
        {
            AnalyzeSyntaxProviderReturn(context, invocation);
        }
    }

    private static void AnalyzeRegisterSourceOutput(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string methodName)
    {
        // RegisterSourceOutput(source, action) - we check the type of 'source'
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 1)
            return;

        // The first argument is the IncrementalValueProvider<T> source
        var sourceArg = args[0];
        var typeInfo = context.SemanticModel.GetTypeInfo(sourceArg.Expression);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;

        if (type == null)
            return;

        CheckTypeForNonDeterminism(context, type, invocation.GetLocation(), isRegisterMethod: true);
    }

    private static void AnalyzeSyntaxProviderReturn(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        // The return type of the invocation is IncrementalValueProvider<T>
        // We want to check T
        var typeInfo = context.SemanticModel.GetTypeInfo(invocation);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;

        if (type == null)
            return;

        // Unwrap IncrementalValueProvider<T> to get T
        if (type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            (namedType.Name == "IncrementalValueProvider" || namedType.Name == "IncrementalValuesProvider"))
        {
            if (namedType.TypeArguments.Length > 0)
            {
                CheckTypeForNonDeterminism(context, namedType.TypeArguments[0],
                    invocation.GetLocation(), isRegisterMethod: false);
            }
        }
    }

    private static void CheckTypeForNonDeterminism(
        SyntaxNodeAnalysisContext context,
        ITypeSymbol type,
        Location location,
        bool isRegisterMethod)
    {
        // Unwrap IncrementalValueProvider<T> and IncrementalValuesProvider<T>
        if (type is INamedTypeSymbol namedTypeOuter &&
            namedTypeOuter.IsGenericType &&
            (namedTypeOuter.Name == "IncrementalValueProvider" || namedTypeOuter.Name == "IncrementalValuesProvider"))
        {
            if (namedTypeOuter.TypeArguments.Length > 0)
                type = namedTypeOuter.TypeArguments[0];
        }

        var typeName = type.Name;
        var fullTypeName = type.ToDisplayString();

        // Check for array types first
        if (type is IArrayTypeSymbol arrayType)
        {
            if (isRegisterMethod)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG007,
                    location,
                    fullTypeName));
            }
            else
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG008,
                    location,
                    fullTypeName));
            }
            return;
        }

        // Check for collection types (List<T>, ImmutableArray<T>, etc.)
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            if (CollectionTypeNames.Contains(namedType.Name))
            {
                if (isRegisterMethod)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG007,
                        location,
                        fullTypeName));
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG008,
                        location,
                        fullTypeName));
                }
                return;
            }
        }

        // Check for known non-deterministic types
        if (NonDeterministicTypeNames.Contains(typeName))
        {
            if (isRegisterMethod)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG006,
                    location,
                    fullTypeName));
            }
            else
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG008,
                    location,
                    fullTypeName));
            }
            return;
        }

        // Check if it implements ISymbol or derives from SyntaxNode
        if (type is INamedTypeSymbol checkType)
        {
            if (ImplementsInterface(checkType, "ISymbol", "Microsoft.CodeAnalysis") ||
                DerivesFrom(checkType, "SyntaxNode", "Microsoft.CodeAnalysis") ||
                DerivesFrom(checkType, "SemanticModel", "Microsoft.CodeAnalysis") ||
                DerivesFrom(checkType, "Compilation", "Microsoft.CodeAnalysis"))
            {
                if (isRegisterMethod)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG006,
                        location,
                        fullTypeName));
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG008,
                        location,
                        fullTypeName));
                }
                return;
            }

            // Check for user-defined classes (not records or structs)
            if (IsUserDefinedMutableClass(checkType))
            {
                if (isRegisterMethod)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG006,
                        location,
                        fullTypeName));
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG008,
                        location,
                        fullTypeName));
                }
            }
        }
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
        // Is a class (not struct, not record, not enum, not interface, not delegate)
        if (type.TypeKind != TypeKind.Class)
            return false;

        // Is not a record
        if (type.IsRecord)
            return false;

        // Is not a system type
        var ns = type.ContainingNamespace?.ToString();
        if (ns != null && (ns.StartsWith("System", StringComparison.Ordinal) ||
                           ns.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)))
            return false;

        // Is not anonymous type
        if (type.IsAnonymousType)
            return false;

        return true;
    }
}
