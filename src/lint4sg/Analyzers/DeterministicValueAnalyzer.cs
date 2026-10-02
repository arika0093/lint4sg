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
/// LSG008: Non-deterministic value retained at an incremental pipeline checkpoint (warning).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeterministicValueAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> RegisterMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "RegisterSourceOutput",
        "RegisterImplementationSourceOutput"
    );

    private static readonly ImmutableHashSet<string> SyntaxProviderMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "CreateSyntaxProvider",
            "ForAttributeWithMetadataName"
        );

    private static readonly ImmutableHashSet<string> PipelineTransformMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal, "Select", "SelectMany", "Combine", "Collect");

    private static readonly string[] RoslynStateMetadataNames =
    {
        "Microsoft.CodeAnalysis.ISymbol",
        "Microsoft.CodeAnalysis.SyntaxNode",
        "Microsoft.CodeAnalysis.SemanticModel",
        "Microsoft.CodeAnalysis.Compilation",
        "Microsoft.CodeAnalysis.Location",
        "Microsoft.CodeAnalysis.SyntaxReference",
        "Microsoft.CodeAnalysis.AdditionalText",
        "Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions",
        "Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider",
    };

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
        "Array"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.LSG006,
            DiagnosticDescriptors.LSG007,
            DiagnosticDescriptors.LSG008
        );

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
        else if (PipelineTransformMethods.Contains(methodName))
        {
            AnalyzePipelineCheckpoint(context, invocation);
        }
    }

    private static void AnalyzePipelineCheckpoint(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var result = GetProviderElementType(context.SemanticModel.GetTypeInfo(invocation).Type);
        if (result == null)
            return;

        // Avoid repeating a diagnostic when a stage merely carries the exact same
        // unsafe value type forward. Report when the checkpoint changes its shape.
        var input = GetProviderElementType(context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type);
        if (input != null && SymbolEqualityComparer.Default.Equals(input, result))
            return;

        CheckTypeForNonDeterminism(
            context,
            result,
            invocation.GetLocation(),
            isRegisterMethod: false,
            new HashSet<string>()
        );
    }

    private static ITypeSymbol? GetProviderElementType(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length > 0
            && (named.Name == "IncrementalValueProvider" || named.Name == "IncrementalValuesProvider"))
            return named.TypeArguments[0];

        return null;
    }

    private static void AnalyzeRegisterSourceOutput(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 1)
            return;

        var sourceArg = args[0];
        var typeInfo = context.SemanticModel.GetTypeInfo(sourceArg.Expression);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;

        if (type == null)
            return;

        CheckTypeForNonDeterminism(
            context,
            type,
            invocation.GetLocation(),
            isRegisterMethod: true,
            new HashSet<string>()
        );
    }

    private static void AnalyzeSyntaxProviderReturn(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(invocation);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;

        if (type == null)
            return;

        if (
            type is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && (
                namedType.Name == "IncrementalValueProvider"
                || namedType.Name == "IncrementalValuesProvider"
            )
        )
        {
            if (namedType.TypeArguments.Length > 0)
            {
                CheckSyntaxProviderElementTypeForNonDeterminism(
                    context,
                    namedType.TypeArguments[0],
                    invocation.GetLocation(),
                    new HashSet<string>()
                );
            }
        }
    }

    private static void CheckSyntaxProviderElementTypeForNonDeterminism(
        SyntaxNodeAnalysisContext context,
        ITypeSymbol type,
        Location location,
        HashSet<string> visitedTypeIds
    )
    {
        switch (type)
        {
            case IArrayTypeSymbol arrayType:
                CheckSyntaxProviderElementTypeForNonDeterminism(
                    context,
                    arrayType.ElementType,
                    location,
                    visitedTypeIds
                );
                return;
            case INamedTypeSymbol namedType
                when namedType.IsGenericType && CollectionTypeNames.Contains(namedType.Name):
                foreach (var typeArgument in namedType.TypeArguments)
                {
                    CheckSyntaxProviderElementTypeForNonDeterminism(
                        context,
                        typeArgument,
                        location,
                        visitedTypeIds
                    );
                }
                return;
        }

        CheckTypeForNonDeterminism(
            context,
            type,
            location,
            isRegisterMethod: false,
            visitedTypeIds
        );
    }

    private static void CheckTypeForNonDeterminism(
        SyntaxNodeAnalysisContext context,
        ITypeSymbol type,
        Location location,
        bool isRegisterMethod,
        HashSet<string> visitedTypeIds
    )
    {
        // Unwrap IncrementalValueProvider<T> / IncrementalValuesProvider<T>
        if (
            type is INamedTypeSymbol namedTypeOuter
            && namedTypeOuter.IsGenericType
            && (
                namedTypeOuter.Name == "IncrementalValueProvider"
                || namedTypeOuter.Name == "IncrementalValuesProvider"
            )
        )
        {
            if (namedTypeOuter.TypeArguments.Length > 0)
                type = namedTypeOuter.TypeArguments[0];
        }

        var fullTypeName = type.ToDisplayString();

        // Primitive/runtime special types are deterministic by themselves, so we can stop here.
        // Strings are handled later because they are the common reference-type exception.
        if (
            type.SpecialType != SpecialType.None
            && type.SpecialType != SpecialType.System_String
            && type.TypeKind != TypeKind.Struct
        )
        {
            return;
        }

        // Arrays -> LSG007 / LSG008
        if (type is IArrayTypeSymbol)
        {
            ReportDiagnostic(
                context,
                location,
                isRegisterMethod,
                DiagnosticDescriptors.LSG007,
                DiagnosticDescriptors.LSG008,
                fullTypeName
            );
            return;
        }

        // Well-known collection types -> LSG007 / LSG008
        if (
            type is INamedTypeSymbol collType
            && collType.IsGenericType
            && CollectionTypeNames.Contains(collType.Name)
        )
        {
            ReportDiagnostic(
                context,
                location,
                isRegisterMethod,
                DiagnosticDescriptors.LSG007,
                DiagnosticDescriptors.LSG008,
                fullTypeName
            );
            return;
        }

        // Compare against Roslyn's actual symbols so a user-defined type named
        // "Compilation" or "SyntaxNode" does not produce a false positive.
        if (IsRoslynStateType(context.Compilation, type))
        {
            ReportDiagnostic(
                context,
                location,
                isRegisterMethod,
                DiagnosticDescriptors.LSG006,
                DiagnosticDescriptors.LSG008,
                fullTypeName
            );
            return;
        }

        if (type is not INamedTypeSymbol checkType)
            return;

        var hasValueEquality = HasValueEquality(checkType);

        // Collection-like types that are not known to have stable value semantics -> LSG007 / LSG008
        if (IsCollectionLike(checkType) && !hasValueEquality)
        {
            ReportDiagnostic(
                context,
                location,
                isRegisterMethod,
                DiagnosticDescriptors.LSG007,
                DiagnosticDescriptors.LSG008,
                fullTypeName
            );
            return;
        }

        // Prevent infinite recursion for self-referential types
        var typeId = checkType.ToDisplayString();
        if (visitedTypeIds.Contains(typeId))
            return;
        visitedTypeIds.Add(typeId);
        var updatedVisited = visitedTypeIds;

        // Tuples: check each element type recursively
        if (checkType.IsTupleType)
        {
            foreach (var element in checkType.TupleElements)
                CheckTypeForNonDeterminism(
                    context,
                    element.Type,
                    location,
                    isRegisterMethod,
                    updatedVisited
                );
            return;
        }

        // Generic type arguments (ValueTuple<T1,T2>, wrapper records, etc.)
        if (checkType.IsGenericType)
        {
            foreach (var typeArg in checkType.TypeArguments)
                CheckTypeForNonDeterminism(
                    context,
                    typeArg,
                    location,
                    isRegisterMethod,
                    updatedVisited
                );
        }

        if (checkType.IsReferenceType && !hasValueEquality)
        {
            ReportDiagnostic(
                context,
                location,
                isRegisterMethod,
                DiagnosticDescriptors.LSG006,
                DiagnosticDescriptors.LSG008,
                fullTypeName
            );
            return;
        }

        // Records, structs, and custom value-equality classes: recursively check all property/field types
        if (checkType.IsRecord || checkType.TypeKind == TypeKind.Struct || hasValueEquality)
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
                    _ => null,
                };
                if (memberType != null)
                {
                    if (IsHiddenCollectionStorage(member, memberType, hasValueEquality, checkType))
                    {
                        CheckContainedTypeArgumentsForNonDeterminism(
                            context,
                            memberType,
                            location,
                            isRegisterMethod,
                            updatedVisited
                        );
                        continue;
                    }

                    CheckTypeForNonDeterminism(
                        context,
                        memberType,
                        location,
                        isRegisterMethod,
                        updatedVisited
                    );
                }
            }
        }
    }

    private static void ReportDiagnostic(
        SyntaxNodeAnalysisContext context,
        Location location,
        bool isRegisterMethod,
        DiagnosticDescriptor registerDescriptor,
        DiagnosticDescriptor syntaxProviderDescriptor,
        string fullTypeName
    )
    {
        context.ReportDiagnostic(
            Diagnostic.Create(
                isRegisterMethod ? registerDescriptor : syntaxProviderDescriptor,
                location,
                fullTypeName
            )
        );
    }

    private static bool IsRoslynStateType(Compilation compilation, ITypeSymbol type)
    {
        foreach (var metadataName in RoslynStateMetadataNames)
        {
            var knownType = compilation.GetTypeByMetadataName(metadataName);
            if (knownType != null
                && !SymbolEqualityComparer.Default.Equals(knownType.ContainingAssembly, compilation.Assembly)
                && IsOrDerivesFrom(type, knownType))
                return true;
        }

        var symbol = compilation.GetTypeByMetadataName("Microsoft.CodeAnalysis.ISymbol");
        return symbol != null
            && !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly)
            && type is INamedTypeSymbol named
            && named.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, symbol));
    }

    private static bool IsOrDerivesFrom(ITypeSymbol type, INamedTypeSymbol knownType)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, knownType))
                return true;
        }

        return false;
    }

    private static bool IsCollectionLike(INamedTypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;

        if (
            type.Name == "ImmutableArray"
            && type.ContainingNamespace?.ToString() == "System.Collections.Immutable"
        )
        {
            return true;
        }

        return type.AllInterfaces.Any(i =>
            i.Name
                is "IEnumerable"
                    or "ICollection"
                    or "IList"
                    or "IReadOnlyCollection"
                    or "IReadOnlyList"
            && i.ContainingNamespace != null
            && i.ContainingNamespace.ToDisplayString()
                .StartsWith("System.Collections", StringComparison.Ordinal)
        );
    }

    private static bool HasValueEquality(INamedTypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return true;

        if (type.TypeKind == TypeKind.Enum || type.IsTupleType)
            return true;

        if (type.TypeKind == TypeKind.Struct)
            return HasImmutableInstanceState(type);

        if (type.TypeKind != TypeKind.Class)
            return false;

        // A custom equality implementation does not make mutable reference state
        // safe for incremental caching. Accept explicit value wrappers only when
        // their instance state is immutable.
        return HasImmutableInstanceState(type)
            && (type.IsRecord || OverridesObjectEquals(type) || ImplementsIEquatableOfSelf(type));
    }

    private static bool HasImmutableInstanceState(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsStatic || member.IsImplicitlyDeclared)
                continue;

            if (member is IFieldSymbol field && !field.IsReadOnly && !field.IsConst)
                return false;
            if (member is IPropertySymbol property
                && property.SetMethod is { IsInitOnly: false })
                return false;
        }

        return true;
    }

    private static bool OverridesObjectEquals(INamedTypeSymbol type)
    {
        return type.GetMembers("Equals")
            .OfType<IMethodSymbol>()
            .Any(m =>
                m is { IsOverride: true, Parameters.Length: 1 }
                && m.Parameters[0].Type.SpecialType == SpecialType.System_Object
            );
    }

    private static bool ImplementsIEquatableOfSelf(INamedTypeSymbol type)
    {
        return type.AllInterfaces.Any(i =>
            i.Name == "IEquatable"
            && i.ContainingNamespace?.ToString() == "System"
            && i is { TypeArguments.Length: 1 }
            && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type)
        );
    }

    private static bool IsHiddenCollectionStorage(
        ISymbol member,
        ITypeSymbol memberType,
        bool hasValueEquality,
        INamedTypeSymbol containingType
    )
    {
        return hasValueEquality
            && HasHiddenCollectionStorageValueEquality(containingType)
            && IsCollectionLike(containingType)
            && member.DeclaredAccessibility == Accessibility.Private
            && IsCollectionStorageType(memberType);
    }

    private static bool HasHiddenCollectionStorageValueEquality(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Class)
            return true;

        return type.TypeKind == TypeKind.Struct
            && (OverridesObjectEquals(type) || ImplementsIEquatableOfSelf(type));
    }

    private static bool IsCollectionStorageType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
            return true;

        if (type is not INamedTypeSymbol namedType)
            return false;

        return (namedType.IsGenericType && CollectionTypeNames.Contains(namedType.Name))
            || IsCollectionLike(namedType);
    }

    private static void CheckContainedTypeArgumentsForNonDeterminism(
        SyntaxNodeAnalysisContext context,
        ITypeSymbol type,
        Location location,
        bool isRegisterMethod,
        HashSet<string> visitedTypeIds
    )
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            CheckTypeForNonDeterminism(
                context,
                arrayType.ElementType,
                location,
                isRegisterMethod,
                visitedTypeIds
            );
            return;
        }

        if (type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
            return;

        foreach (var typeArgument in namedType.TypeArguments)
        {
            CheckTypeForNonDeterminism(
                context,
                typeArgument,
                location,
                isRegisterMethod,
                visitedTypeIds
            );
        }
    }
}
