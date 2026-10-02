using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>LSG023: Rejects mutable instance state on incremental generators.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatelessGeneratorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.LSG023);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || !IsIncrementalGenerator(context.Compilation, type))
            return;

        for (var current = type; current != null; current = current.BaseType)
        {
            // A generator base type is analyzed on its own. Continue through
            // ordinary base classes so state inherited by the generator is covered.
            if (!SymbolEqualityComparer.Default.Equals(current, type)
                && IsIncrementalGenerator(context.Compilation, current))
                break;

            foreach (var member in current.GetMembers())
            {
                if (member.IsImplicitlyDeclared)
                    continue;

                var mutable = member switch
                {
                    IFieldSymbol field => !field.IsConst
                        && (!field.IsReadOnly || IsMutableStorage(field.Type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default))),
                    IPropertySymbol property => property.SetMethod is { IsInitOnly: false }
                        || IsMutableStorage(property.Type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)),
                    _ => false,
                };

                if (mutable)
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.LSG023, member.Locations.FirstOrDefault(), member.Name));
            }
        }
    }

    private static bool IsMutableStorage(ITypeSymbol type, HashSet<ITypeSymbol> visited)
    {
        if (type is IArrayTypeSymbol)
            return true;

        if (type is not INamedTypeSymbol namedType)
            return false;

        var ns = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns.StartsWith("System.Collections", StringComparison.Ordinal)
            && namedType.Name is "List" or "Dictionary" or "HashSet" or "Queue" or "Stack"
                or "SortedList" or "SortedDictionary" or "ConcurrentBag" or "ConcurrentQueue"
                or "ConcurrentStack" or "ConcurrentDictionary")
            return true;

        if (namedType.Name is "ICollection" or "IList" or "IDictionary" or "ISet"
            && ns.StartsWith("System.Collections", StringComparison.Ordinal))
            return true;

        if (ns == "System.Collections.Immutable")
            return namedType.TypeArguments.Any(argument => IsMutableStorage(argument, visited));

        if (namedType.SpecialType != SpecialType.None || namedType.TypeKind == TypeKind.Enum)
            return false;

        if (!visited.Add(namedType.OriginalDefinition))
            return false;

        if (namedType.AllInterfaces.Any(i =>
            i.Name is "ICollection" or "IList" or "IDictionary"
                && i.ContainingNamespace?.ToDisplayString().StartsWith("System.Collections", StringComparison.Ordinal) == true))
            return true;

        foreach (var member in namedType.GetMembers())
        {
            if (member.IsStatic || member.IsImplicitlyDeclared)
                continue;

            if (member is IFieldSymbol field && (!field.IsReadOnly || IsMutableStorage(field.Type, visited)))
                return true;
            if (member is IPropertySymbol property
                && (property.SetMethod is { IsInitOnly: false }
                    || IsMutableStorage(property.Type, visited)))
                return true;
        }

        return false;
    }

    private static bool IsIncrementalGenerator(Compilation compilation, INamedTypeSymbol type)
    {
        var generatorInterface = compilation.GetTypeByMetadataName("Microsoft.CodeAnalysis.IIncrementalGenerator");
        return generatorInterface != null
            && type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, generatorInterface));
    }
}
