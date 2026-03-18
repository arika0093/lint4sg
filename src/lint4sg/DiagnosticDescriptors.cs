using Microsoft.CodeAnalysis;

namespace lint4sg;

internal static class DiagnosticDescriptors
{
    private const string SourceGeneratorCategory = "SourceGenerator";

    // LSG001: ISourceGenerator usage
    public static readonly DiagnosticDescriptor LSG001 = new(
        id: "LSG001",
        title: "Avoid ISourceGenerator",
        messageFormat: "Use IIncrementalGenerator instead of ISourceGenerator for better performance",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ISourceGenerator is deprecated. IIncrementalGenerator provides better performance through incremental processing.");

    // LSG002: Non-FAWMN SyntaxProvider usage
    public static readonly DiagnosticDescriptor LSG002 = new(
        id: "LSG002",
        title: "Prefer ForAttributeWithMetadataName",
        messageFormat: "Prefer ForAttributeWithMetadataName over CreateSyntaxProvider for better performance. Use CreateSyntaxProvider only when attribute-based filtering is not applicable.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ForAttributeWithMetadataName (FAWMN) is optimized to filter nodes efficiently. Use CreateSyntaxProvider only when FAWMN is not applicable.");

    // LSG003: High-cost SyntaxProvider predicate
    public static readonly DiagnosticDescriptor LSG003 = new(
        id: "LSG003",
        title: "Avoid high-cost SyntaxProvider predicate",
        messageFormat: "Checking interface/base-class/attribute inheritance inside SyntaxProvider predicate is expensive. Consider using ForAttributeWithMetadataName combined with semantic checks in the transform step.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Interface/base-class/attribute inheritance checks inside SyntaxProvider predicates are expensive because they run on every syntax change. Use ForAttributeWithMetadataName to pre-filter and perform semantic checks in the transform step only.");

    // LSG004: CancellationToken not forwarded
    public static readonly DiagnosticDescriptor LSG004 = new(
        id: "LSG004",
        title: "Forward CancellationToken",
        messageFormat: "CancellationToken must be forwarded to all methods that accept it",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a CancellationToken is available, it must be forwarded to all invoked methods that accept a CancellationToken parameter to support proper cancellation.");

    // LSG005: Missing ThrowIfCancellationRequested in loop
    public static readonly DiagnosticDescriptor LSG005 = new(
        id: "LSG005",
        title: "Missing ThrowIfCancellationRequested in loop",
        messageFormat: "Call ThrowIfCancellationRequested() in each loop iteration when CancellationToken is available",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a CancellationToken is available and the method contains loops, ThrowIfCancellationRequested() must be called in each iteration to support responsive cancellation.");

    // LSG006: Non-deterministic value in RegisterSourceOutput (non-collection)
    public static readonly DiagnosticDescriptor LSG006 = new(
        id: "LSG006",
        title: "Non-deterministic value in RegisterSourceOutput",
        messageFormat: "RegisterSourceOutput receives a non-deterministic value of type '{0}'. Use equatable types (records, structs, or primitives) to enable proper caching.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "RegisterSourceOutput should receive equatable/deterministic values to enable Roslyn's caching mechanism. Non-deterministic types like ISymbol, SyntaxNode, SemanticModel, Compilation, or mutable classes will cause unnecessary regeneration.");

    // LSG007: Non-deterministic collection in RegisterSourceOutput
    public static readonly DiagnosticDescriptor LSG007 = new(
        id: "LSG007",
        title: "Non-deterministic collection in RegisterSourceOutput",
        messageFormat: "RegisterSourceOutput receives a collection/array type '{0}'. Arrays and lists use reference equality which defeats caching. Use an equatable collection type instead.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Arrays and List<T>/ImmutableArray<T> use reference equality, which defeats Roslyn's caching mechanism in RegisterSourceOutput. Use EquatableArray<T> or similar equatable collection.");

    // LSG008: Non-deterministic SyntaxProvider return value
    public static readonly DiagnosticDescriptor LSG008 = new(
        id: "LSG008",
        title: "Non-deterministic SyntaxProvider return value",
        messageFormat: "SyntaxProvider transform returns a non-deterministic type '{0}'. This may cause unnecessary regeneration. Use equatable types for better caching.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SyntaxProvider transforms should return equatable/deterministic types to enable Roslyn's caching. Non-deterministic types may cause the pipeline to re-execute unnecessarily.");

    // LSG009: NormalizeWhitespace usage
    public static readonly DiagnosticDescriptor LSG009 = new(
        id: "LSG009",
        title: "Avoid NormalizeWhitespace",
        messageFormat: "NormalizeWhitespace() is expensive. Use an indented writer (IndentedTextWriter or similar) instead.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "NormalizeWhitespace() traverses the entire syntax tree which is expensive. Use IndentedTextWriter or a similar indentation-aware writer for code generation.");

    // LSG010: Excessive whitespace in AppendLine
    public static readonly DiagnosticDescriptor LSG010 = new(
        id: "LSG010",
        title: "Excessive whitespace in AppendLine",
        messageFormat: "AppendLine/Append contains excessive whitespace indentation. Use a dedicated StringBuilder with indentation management instead.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Using 8+ consecutive spaces or 2+ consecutive tabs in AppendLine/Append calls indicates manual indentation management. Use IndentedStringBuilder or a similar utility for better maintainability.");

    // LSG011: Consecutive AppendLine calls
    public static readonly DiagnosticDescriptor LSG011 = new(
        id: "LSG011",
        title: "Use raw string literal instead of consecutive AppendLine",
        messageFormat: "Use a raw string literal ($$\"\"\"...\"\"\"]) instead of {0} or more consecutive AppendLine calls for multi-line code generation",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Three or more consecutive AppendLine calls without branching should be replaced with a raw string literal ($$\"\"\"...\"\"\"]) for better readability and maintainability.");

    // LSG012: External dependency usage
    public static readonly DiagnosticDescriptor LSG012 = new(
        id: "LSG012",
        title: "External dependency in source generator",
        messageFormat: "External NuGet package '{0}' detected. External dependencies in source generators complicate NuGet packaging. Consider avoiding external dependencies.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Source generators with external NuGet dependencies require complex packaging. Consider inlining or avoiding external dependencies.");

    // LSG013: Reflection API usage
    public static readonly DiagnosticDescriptor LSG013 = new(
        id: "LSG013",
        title: "Avoid Reflection API in source generator",
        messageFormat: "Reflection API usage detected ('{0}'). Source generators should generate static code without using reflection, which defeats the purpose of compile-time code generation.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Using reflection in a source generator defeats the performance and correctness benefits of compile-time code generation. Generate static code instead of using reflection.");

    // LSG014: Microsoft.CodeAnalysis.CSharp version too new
    public static readonly DiagnosticDescriptor LSG014 = new(
        id: "LSG014",
        title: "Microsoft.CodeAnalysis.CSharp version may be too new",
        messageFormat: "Microsoft.CodeAnalysis.CSharp version '{0}' may be too new. Lower versions support more environments. Consider using version < 5.0.0.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Using a newer version of Microsoft.CodeAnalysis.CSharp limits the environments where the generator can run. Consider targeting a lower version for maximum compatibility.");

}
