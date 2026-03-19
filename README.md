# lint4sg

[![NuGet Version](https://img.shields.io/nuget/v/lint4sg?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/lint4sg/)
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/lint4sg/test.yaml?branch=main&label=Test&style=flat-square)

A strict Roslyn analyzer that enforces best practices for **.NET Source Generator** projects.

Source Generators have strict performance and correctness requirements that differ significantly from ordinary C# code. This analyzer catches common mistakes at compile time, acting as a safety net especially when AI coding assistants generate Source Generator code.

## Installation

Add `lint4sg` as a project reference in your Source Generator project:

```bash
dotnet add package lint4sg
```

## Analyzer Rules

| ID | Severity | Title | Description |
|----|----------|-------|-------------|
| [LSG001](#lsg001) | Error | Avoid ISourceGenerator | Use `IIncrementalGenerator` instead |
| [LSG002](#lsg002) | Warning | Prefer ForAttributeWithMetadataName | `FAWMN` is heavily optimised over `CreateSyntaxProvider` |
| [LSG003](#lsg003) | Error | Avoid high-cost SyntaxProvider predicate | Inheritance checks in predicates are expensive |
| [LSG004](#lsg004) | Error | Forward CancellationToken | CT must be forwarded to all accepting callees |
| [LSG005](#lsg005) | Error | Missing ThrowIfCancellationRequested in loop | Loops must check CT on each iteration |
| [LSG006](#lsg006) | Error | Non-deterministic value in RegisterSourceOutput | Non-equatable types defeat Roslyn caching |
| [LSG007](#lsg007) | Error | Non-deterministic collection in RegisterSourceOutput | Arrays/lists use reference equality |
| [LSG008](#lsg008) | Warning | Non-deterministic SyntaxProvider return value | May cause unnecessary pipeline re-execution |
| [LSG009](#lsg009) | Error | Avoid NormalizeWhitespace | O(n) syntax-tree traversal — use a writer instead |
| [LSG010](#lsg010) | Error | Excessive whitespace in AppendLine | Use indentation-aware StringBuilder |
| [LSG011](#lsg011) | Error | Use raw string literal | Replace 3+ consecutive AppendLine calls |
| [LSG012](#lsg012) | Warning | External dependency in source generator | Complicates NuGet packaging |
| [LSG013](#lsg013) | Warning | Avoid Reflection API in source generator | Defeats compile-time code generation benefits |
| [LSG014](#lsg014) | Warning | CodeAnalysis.CSharp version may be too new | Lower versions support more environments |
| [LSG015](#lsg015) | Error | Avoid fully-indented raw string output | Raw string output should not prepend indentation on every line |
| [LSG016](#lsg016) | Error | Avoid allocations in syntax provider predicate | Predicates run hot and must stay allocation-free |

---

## Rule Details

### LSG001

**Avoid ISourceGenerator — use IIncrementalGenerator**

`ISourceGenerator` (V1) runs on every keystroke and cannot cache results. It was superseded by `IIncrementalGenerator` (V2) which uses a pipeline model that only re-runs stages when their inputs change.

```csharp
// ❌ LSG001
[Generator]
public class MyGenerator : ISourceGenerator { ... }

// ✅ OK
[Generator]
public class MyGenerator : IIncrementalGenerator { ... }
```

Source: [Incremental Generators design document](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)

---

### LSG002

**Prefer ForAttributeWithMetadataName over CreateSyntaxProvider** *(warning)*

`SyntaxProvider.ForAttributeWithMetadataName` (FAWMN) is a specially optimised method that filters nodes based on attribute names using a fast path that avoids most of the syntax tree. `CreateSyntaxProvider` is a general-purpose fallback; avoid it unless FAWMN is not applicable.

```csharp
// ⚠️ LSG002
var result = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: (node, ct) => node is ClassDeclarationSyntax,
    transform: (ctx, ct) => (ClassDeclarationSyntax)ctx.Node);

// ✅ OK
var result = context.SyntaxProvider.ForAttributeWithMetadataName(
    "MyNamespace.MyAttribute",
    predicate: (node, ct) => node is ClassDeclarationSyntax,
    transform: (ctx, ct) => (ClassDeclarationSyntax)ctx.Node);
```

Source: [Source Generators overview](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.md), [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)

---

### LSG003

**Avoid broad inheritance scans in CreateSyntaxProvider**

Inheritance checks (`.Interfaces`, `.BaseType`, `IsAssignableTo`, `.GetAttributes()`, etc.) inside `CreateSyntaxProvider` are expensive. Doing them in the predicate is bad, and doing them in the transform via `GetDeclaredSymbol(...)` is still a broad semantic scan unless you already narrowed the pipeline with a marker-based pre-filter such as `ForAttributeWithMetadataName`.

```csharp
// ❌ LSG003
var result = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: (node, ct) => node.GetType().BaseType != null, // expensive!
    transform: (ctx, ct) => ctx.Node);

// ❌ LSG003 — GetDeclaredSymbol-based inheritance checks in the transform
// are still a broad scan when CreateSyntaxProvider is the only filter.
var result = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: (node, ct) => node is ClassDeclarationSyntax,
    transform: (ctx, ct) => {
        var cls = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(cls, ct);
        return symbol?.BaseType?.Name == "MyBase" ? symbol : null;
    });

// ✅ OK — pre-filter first, then perform semantic checks on the matched nodes only
var result = context.SyntaxProvider.ForAttributeWithMetadataName(
    "MyNamespace.MyMarkerAttribute",
    predicate: (node, ct) => node is ClassDeclarationSyntax,
    transform: (ctx, ct) => {
        var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
        return symbol.BaseType?.Name == "MyBase" ? symbol : null;
    });
```

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md), [Avoiding performance pitfalls in incremental generators](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/)

---

### LSG004

**Add CancellationToken to helpers in source-generator call trees**

When a source-generator callback receives a `CancellationToken`, walk the helper call tree from that callback. If the tree reaches cancellation-aware work — such as loops or external APIs that offer a `CancellationToken` overload — then every project helper in that path should accept `CancellationToken`.

```csharp
// ❌ LSG004
void Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
{
    Parse();
}

void Parse()
{
    foreach (var item in items)
    {
    }
}

// ✅ OK
void Parse(CancellationToken ct)
{
    foreach (var item in items)
    {
        ct.ThrowIfCancellationRequested();
    }
}
```

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)

---

### LSG005

**Use available CancellationToken inside the call tree**

After a helper accepts `CancellationToken`, it must keep using that token correctly:

- pass it to CT-aware child calls
- call `ThrowIfCancellationRequested()` inside loops

```csharp
// ❌ LSG005
void Process(IEnumerable<ISymbol> symbols, CancellationToken ct)
{
    foreach (var symbol in symbols) // missing ThrowIfCancellationRequested!
    {
        Generate(symbol, ct);
    }
}

// ✅ OK
void Process(IEnumerable<ISymbol> symbols, CancellationToken ct)
{
    foreach (var symbol in symbols)
    {
        ct.ThrowIfCancellationRequested();
        Generate(symbol, ct);
    }
}
```

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)

---

### LSG006

**Non-deterministic value in RegisterSourceOutput**

`RegisterSourceOutput` caches results based on value equality of the input. Types like `ISymbol`, `SyntaxNode`, `SemanticModel`, `Compilation`, and ordinary reference types without value equality defeat that cache and force unnecessary re-execution. This also applies to **nested** members — if a record or custom equatable class contains an `ISymbol` field, the whole value is still non-deterministic.

Use records, structs, primitives, or carefully-designed value-equality classes whose members are themselves deterministic.

```csharp
// ❌ LSG006 — ISymbol inside record defeats caching
public record MyInfo(string Name, ISymbol Symbol);
context.RegisterSourceOutput(provider, (spc, info) => Generate(spc, info));

// ✅ OK — extract only the data you need
public record MyInfo(string Name, string FullName);
context.RegisterSourceOutput(provider, (spc, info) => Generate(spc, info));
```

Source: [Incremental Generators design document](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md), [Avoiding performance pitfalls in incremental generators](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/)

---

### LSG007

**Non-deterministic collection in RegisterSourceOutput**

Arrays (`T[]`) and `List<T>` use reference equality. Even if two arrays have the same contents, they are not equal by default. Use an equatable collection such as `EquatableArray<T>` (from the [Andrew Lock blog](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/)).

```csharp
// ❌ LSG007 — arrays use reference equality
context.RegisterSourceOutput(arrayProvider, (spc, arr) => Generate(spc, arr));

// ✅ OK — use an equatable wrapper
context.RegisterSourceOutput(equatableArrayProvider, (spc, arr) => Generate(spc, arr));
```

Source: [Avoiding performance pitfalls in incremental generators](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/)

---

### LSG008

**Non-deterministic SyntaxProvider return value** *(warning)*

The same equatability requirements from LSG006/LSG007 apply to the values returned by `ForAttributeWithMetadataName` and `CreateSyntaxProvider` transforms. If the transform returns a non-equatable type, Roslyn's caching will not work. This is a warning rather than an error because some unavoidable patterns exist.

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md), [Avoiding performance pitfalls in incremental generators](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/)

---

### LSG009

**Avoid NormalizeWhitespace**

`SyntaxNode.NormalizeWhitespace()` traverses the entire syntax tree to re-format all trivia. This is an O(n) operation and is expensive for large trees. Use `IndentedTextWriter` or a custom indented `StringBuilder` instead.

```csharp
// ❌ LSG009
var formatted = compilationUnit.NormalizeWhitespace();

// ✅ OK
using var writer = new IndentedTextWriter(new StringWriter());
writer.Indent++;
writer.WriteLine("public class Foo { }");
```

Source: [Source Generators overview](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.md)

---

### LSG010

**Excessive whitespace in AppendLine**

Manually indenting code with 8 or more consecutive spaces (or 2+ tabs) in `AppendLine` is fragile and hard to maintain. Use an indentation-aware `StringBuilder` utility. Raw string literals are ignored by this rule because they are usually the preferred replacement.

```csharp
// ❌ LSG010
sb.AppendLine("        public void Method()");

// ✅ OK — use IndentedStringBuilder or IndentedTextWriter
sb.AppendLine("public void Method()");
```

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)

---

### LSG011

**Use raw string literal instead of consecutive AppendLine**

Three or more consecutive `AppendLine` calls without any branching should be a raw string literal. Raw string literals (`$$"""..."""`) are more readable and less error-prone.

```csharp
// ❌ LSG011
sb.AppendLine("namespace Foo");
sb.AppendLine("{");
sb.AppendLine("    public class Bar { }");

// ✅ OK
sb.Append($$"""
    namespace {{namespaceName}}
    {
        public class {{className}} { }
    }
    """);
```

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)

---

### LSG012

**External dependency in source generator** *(warning)*

Source generators distributed as NuGet packages require all their transitive dependencies to be bundled (using `PrivateAssets="all"` or similar). This complicates packaging. Prefer inlining small utilities or avoiding external dependencies altogether.

References that are intentionally source-only / analyzer-only and are marked with `PrivateAssets="all"` are allowed.

Source: [Source Generators overview](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.md)

---

### LSG015

**Avoid fully-indented raw string output**

Raw string literals are excellent for generator output, but if **every emitted line** starts with indentation whitespace then the generated text becomes harder to control and maintain. Keep the *source code* indented by aligning the closing delimiter, but avoid baking shared leading whitespace into every output line.

```csharp
// ❌ LSG015
sb.Append("""
        public class Foo
        {
        }
    """);

// ✅ OK
sb.Append("""
public class Foo
{
}
""");
```

---

### LSG016

**Avoid allocations in syntax provider predicate**

`CreateSyntaxProvider` and `ForAttributeWithMetadataName` predicates run extremely often, so even small allocations add up. Keep predicates allocation-free and move allocations to the transform step or outside the pipeline hot path.

```csharp
// ❌ LSG016
var result = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: (node, ct) => new[] { node }.Length > 0,
    transform: (ctx, ct) => ctx);

// ✅ OK
var result = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: (node, ct) => node is ClassDeclarationSyntax,
    transform: (ctx, ct) => new[] { ctx });
```

---

### LSG013

**Avoid Reflection API in source generator** *(warning)*

Using `System.Reflection` in a source generator (or generating code that uses reflection) defeats the purpose of compile-time code generation. Source generators should produce static code. When an LLM generates reflection-based code in a generator, this rule fires immediately to draw attention.

Source: [Source Generators overview](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.md)

---

### LSG014

**Microsoft.CodeAnalysis.CSharp version may be too new** *(warning)*

A source generator that references a newer version of `Microsoft.CodeAnalysis.CSharp` will only run in IDEs and build tools that ship that version. Using a lower version (< 5.0.0 as of March 2026) maximises compatibility with older Visual Studio / Rider installations and CI agents.

Source: [Source Generators overview](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.md)

---

## References

- [Roslyn Incremental Generators design document](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)
- [Roslyn Source Generators overview](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.md)
- [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)
- [Avoiding performance pitfalls in incremental generators](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/)

## License

This project is licensed under the MIT License.
