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
        description: "ISourceGenerator is deprecated. IIncrementalGenerator provides better performance through incremental processing."
    );

    // LSG002: Non-FAWMN SyntaxProvider usage
    public static readonly DiagnosticDescriptor LSG002 = new(
        id: "LSG002",
        title: "Prefer ForAttributeWithMetadataName",
        messageFormat: "Prefer ForAttributeWithMetadataName over CreateSyntaxProvider for better performance. Use CreateSyntaxProvider only when attribute-based filtering is not applicable, or mark an intentional use with 'lint4sg-allow-create-syntax-provider'.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ForAttributeWithMetadataName (FAWMN) is optimized to filter nodes efficiently. Use CreateSyntaxProvider only when FAWMN is not applicable. To acknowledge an intentional exception, add a nearby comment containing 'lint4sg-allow-create-syntax-provider'."
    );

    // LSG003: High-cost CreateSyntaxProvider inheritance scan
    public static readonly DiagnosticDescriptor LSG003 = new(
        id: "LSG003",
        title: "Avoid broad inheritance scans in CreateSyntaxProvider",
        messageFormat: "Checking interface/base-class/attribute inheritance in CreateSyntaxProvider is expensive. Pre-filter first (typically with ForAttributeWithMetadataName) instead of using GetDeclaredSymbol-based scans.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Interface/base-class/attribute inheritance checks in CreateSyntaxProvider are expensive. Predicate checks always run on every syntax change, and transform checks that rely on GetDeclaredSymbol still represent a broad scan unless the pipeline was pre-filtered first. Use ForAttributeWithMetadataName or another narrow pre-filter before performing semantic checks."
    );

    // LSG004: CancellationToken parameter missing on helper
    public static readonly DiagnosticDescriptor LSG004 = new(
        id: "LSG004",
        title: "Add CancellationToken to helper",
        messageFormat: "Add a CancellationToken parameter to '{0}' because its source-generator call tree reaches cancellation-aware work (child calls with CT support or loops).",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a source-generator callback receives a CancellationToken, project helpers in that call tree must also accept it if the tree eventually reaches cancellation-aware work such as CT-capable external APIs or loops."
    );

    // LSG005: CancellationToken available but not used correctly
    public static readonly DiagnosticDescriptor LSG005 = new(
        id: "LSG005",
        title: "Use available CancellationToken",
        messageFormat: "Use the available CancellationToken in this call tree: pass it to child calls and check it inside loops.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "After a helper accepts a CancellationToken, it must keep forwarding that token to CT-aware child calls and call ThrowIfCancellationRequested() inside loops."
    );

    // LSG006: Non-deterministic value in RegisterSourceOutput (non-collection)
    public static readonly DiagnosticDescriptor LSG006 = new(
        id: "LSG006",
        title: "Non-deterministic value in RegisterSourceOutput",
        messageFormat: "RegisterSourceOutput receives a non-deterministic value of type '{0}'. Use equatable types (records, structs, or primitives) to enable proper caching.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "RegisterSourceOutput should receive equatable/deterministic values to enable Roslyn's caching mechanism. Non-deterministic types like ISymbol, SyntaxNode, SemanticModel, Compilation, or mutable classes will cause unnecessary regeneration."
    );

    // LSG007: Non-deterministic collection in RegisterSourceOutput
    public static readonly DiagnosticDescriptor LSG007 = new(
        id: "LSG007",
        title: "Non-deterministic collection in RegisterSourceOutput",
        messageFormat: "RegisterSourceOutput receives a collection/array type '{0}'. Arrays and lists use reference equality which defeats caching. Use an equatable collection type instead.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Arrays and List<T>/ImmutableArray<T> use reference equality, which defeats Roslyn's caching mechanism in RegisterSourceOutput. Use EquatableArray<T> or similar equatable collection."
    );

    // LSG008: Non-deterministic SyntaxProvider return value
    public static readonly DiagnosticDescriptor LSG008 = new(
        id: "LSG008",
        title: "Non-deterministic SyntaxProvider return value",
        messageFormat: "SyntaxProvider transform returns a non-deterministic type '{0}'. This may cause unnecessary regeneration. Use equatable types for better caching.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SyntaxProvider transforms should return equatable/deterministic types to enable Roslyn's caching. Non-deterministic types may cause the pipeline to re-execute unnecessarily."
    );

    // LSG009: NormalizeWhitespace usage
    public static readonly DiagnosticDescriptor LSG009 = new(
        id: "LSG009",
        title: "Avoid NormalizeWhitespace",
        messageFormat: "NormalizeWhitespace() is expensive. Use an indented writer (IndentedTextWriter or similar) instead.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "NormalizeWhitespace() traverses the entire syntax tree which is expensive. Use IndentedTextWriter or a similar indentation-aware writer for code generation."
    );

    // LSG010: Excessive whitespace in AppendLine
    public static readonly DiagnosticDescriptor LSG010 = new(
        id: "LSG010",
        title: "Excessive whitespace in AppendLine",
        messageFormat: "AppendLine/Append contains excessive whitespace indentation. Use a dedicated StringBuilder with indentation management instead.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Using 8+ consecutive spaces or 2+ consecutive tabs in AppendLine/Append calls indicates manual indentation management. Use IndentedStringBuilder or a similar utility for better maintainability."
    );

    // LSG011: Consecutive AppendLine calls
    public static readonly DiagnosticDescriptor LSG011 = new(
        id: "LSG011",
        title: "Use raw string literal instead of consecutive AppendLine",
        messageFormat: "Use a raw string literal ($$\"\"\"...\"\"\"]) instead of {0} or more consecutive AppendLine calls for multi-line code generation",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Three or more consecutive AppendLine calls without branching should be replaced with a raw string literal ($$\"\"\"...\"\"\"]) for better readability and maintainability."
    );

    // LSG012: External dependency usage
    public static readonly DiagnosticDescriptor LSG012 = new(
        id: "LSG012",
        title: "External dependency in source generator",
        messageFormat: "External NuGet package '{0}' detected. External dependencies in source generators complicate NuGet packaging. Consider avoiding external dependencies.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Source generators with external NuGet dependencies require complex packaging. Consider inlining or avoiding external dependencies."
    );

    // LSG013: Reflection API usage
    public static readonly DiagnosticDescriptor LSG013 = new(
        id: "LSG013",
        title: "Avoid Reflection API in source generator",
        messageFormat: "Reflection API usage detected ('{0}'). Source generators should generate static code without using reflection, which defeats the purpose of compile-time code generation.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Using reflection in a source generator defeats the performance and correctness benefits of compile-time code generation. Generate static code instead of using reflection."
    );

    // LSG014: Microsoft.CodeAnalysis.CSharp version too new
    public static readonly DiagnosticDescriptor LSG014 = new(
        id: "LSG014",
        title: "Microsoft.CodeAnalysis.CSharp version may be too new",
        messageFormat: "Microsoft.CodeAnalysis.CSharp version '{0}' may be too new. Lower versions support more environments. Consider using version < 5.0.0.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Using a newer version of Microsoft.CodeAnalysis.CSharp limits the environments where the generator can run. Consider targeting a lower version for maximum compatibility."
    );

    // LSG015: Fully-indented raw string literal
    public static readonly DiagnosticDescriptor LSG015 = new(
        id: "LSG015",
        title: "Avoid fully-indented raw string literal output",
        messageFormat: "This raw string literal emits indentation on every line. Remove the shared leading whitespace from the string contents, and let the raw-string closing delimiter control source indentation instead.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Raw string literals are good for multi-line generation, but if every emitted line starts with indentation then the generated output becomes fragile. Keep the source indented via the closing delimiter and avoid baking shared leading whitespace into the output itself."
    );

    // LSG016: Allocation inside syntax provider predicate
    public static readonly DiagnosticDescriptor LSG016 = new(
        id: "LSG016",
        title: "Avoid allocations in syntax provider predicate",
        messageFormat: "Do not allocate inside SyntaxProvider predicates. Move allocations out of the predicate or into a later transform step.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "CreateSyntaxProvider and ForAttributeWithMetadataName predicates run extremely often. Allocations inside predicates add avoidable overhead and should be moved out of the predicate or deferred to later pipeline stages."
    );

    // LSG017: Pipeline callbacks should be static when possible
    public static readonly DiagnosticDescriptor LSG017 = new(
        id: "LSG017",
        title: "Pipeline callbacks must be static",
        messageFormat: "This pipeline callback does not capture enclosing state and can be marked static",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Incremental-generator pipeline callbacks should be static whenever they do not capture enclosing locals, parameters, or instance state. Static callbacks prevent accidental captures in hot paths."
    );

    // LSG018: Prefer SelectMany over materialized collections in the pipeline
    public static readonly DiagnosticDescriptor LSG018 = new(
        id: "LSG018",
        title: "Prefer SelectMany over materialized collections in the pipeline",
        messageFormat: "This pipeline materializes '{0}' only to flatten or iterate it in the next stage. Prefer SelectMany to keep item granularity.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Avoid carrying arrays, List<T>, or ImmutableArray<T> through the pipeline when the next stage only flattens or iterates them. Prefer SelectMany so items stay granular."
    );

    // LSG019: Delay Collect
    public static readonly DiagnosticDescriptor LSG019 = new(
        id: "LSG019",
        title: "Delay Collect",
        messageFormat: "Collect() happens before item-level filtering or projection. Apply Where, Select, or SelectMany before Collect.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Call Collect() only near the point where whole-set aggregation is actually needed. Item-level filtering or projection should stay upstream."
    );

    // LSG020: Nested tuple proliferation in pipeline composition
    public static readonly DiagnosticDescriptor LSG020 = new(
        id: "LSG020",
        title: "Nested tuple proliferation in pipeline composition",
        messageFormat: "Avoid nested Left or Right tuple navigation and nested tuple construction in pipeline callbacks. Flatten the model or introduce a named type.",
        category: SourceGeneratorCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Repeated Left/Right tuple navigation and nested tuple construction make incremental pipelines hard to read and maintain. Flatten the composition or introduce named intermediate models."
    );
}
