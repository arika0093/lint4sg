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
| [LSG017](#lsg017) | Error | Pipeline callbacks must be static | Prevent accidental captures in incremental hot paths |
| [LSG018](#lsg018) | Error | Prefer SelectMany over materialized collections in the pipeline | Keep item granularity instead of carrying arrays or lists |
| [LSG019](#lsg019) | Error | Delay Collect | Apply item-level filtering and projection before whole-set aggregation |
| [LSG020](#lsg020) | Error | Nested tuple proliferation in pipeline composition | Avoid repeated `Left` / `Right` tuple navigation |

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

Source: [Roslyn Source Generators Document](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/source-generators.md)

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

If you have a valid reason to use `CreateSyntaxProvider` (e.g. you need to analyze function call arguments which cannot be filtered by attributes), you can explicitly opt out of this rule.

```csharp
// lint4sg-allow-create-syntax-provider: (reason)
var result = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: (node, ct) => node is ClassDeclarationSyntax,
    transform: (ctx, ct) => (ClassDeclarationSyntax)ctx.Node);
```

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/incremental-generators.cookbook.md#use-forattributewithmetadataname)

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
Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/incremental-generators.cookbook.md#do-not-scan-for-types-that-indirectly-implement-interfaces-indirectly-inherit-from-types-or-are-indirectly-marked-by-an-attribute-from-an-interface-or-base-type)

---

### LSG004

**Add CancellationToken to helpers in source-generator call trees**

When a source-generator callback receives a `CancellationToken`, walk the helper call tree from that callback. If the tree reaches cancellation-aware work — such as loops or external APIs that offer a `CancellationToken` overload — then every project helper in that path should accept `CancellationToken`.

```csharp
void Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
{
    Parse();
}

void Parse() // ❌ LSG004
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

Source: [Incremental Generators](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/incremental-generators.md#handling-cancellation)

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

Source: [Incremental Generators](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/incremental-generators.md#handling-cancellation)

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

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/incremental-generators.cookbook.md#pipeline-model-design), [Avoiding performance pitfalls in incremental generators](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/)

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

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/incremental-generators.cookbook.md#pipeline-model-design), [Avoiding performance pitfalls in incremental generators](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/)

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

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/incremental-generators.cookbook.md#use-an-indented-text-writer-not-syntaxnodes-for-generation), [Roslyn Issue #52914](https://github.com/dotnet/roslyn/issues/52914#issuecomment-1739939379)

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

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/incremental-generators.cookbook.md), [Roslyn Issue #52914](https://github.com/dotnet/roslyn/issues/52914#issuecomment-1739939379)

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

---

### LSG012

**External dependency in source generator** *(warning)*

Source generators distributed as NuGet packages require all their transitive dependencies to be bundled (using `PrivateAssets="all"` or similar). This complicates packaging. Prefer inlining small utilities or avoiding external dependencies altogether.

References that are intentionally source-only / analyzer-only and are marked with `PrivateAssets="all"` are allowed.

Source: [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/216a7f2f17633d4eea15c15c68f2bfdcdb797f0f/docs/features/incremental-generators.cookbook.md#use-functionality-from-nuget-packages)

---

### LSG013

**Avoid Reflection API in source generator** *(warning)*

Using `System.Reflection` in a source generator (or generating code that uses reflection) defeats the purpose of compile-time code generation. Source generators should produce static code. When an LLM generates reflection-based code in a generator, this rule fires immediately to draw attention.


---

### LSG014

**Microsoft.CodeAnalysis.CSharp version may be too new** *(warning)*

A source generator that references a newer version of `Microsoft.CodeAnalysis.CSharp` will only run in IDEs and build tools that ship that version. Using a lower version (< 5.0.0 as of March 2026) maximises compatibility with older Visual Studio / Rider installations and CI agents.

Also, while we cannot directly warn about this, if you are using dependabot, we recommend explicitly excluding it from updates.

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
    ignore:
      - dependency-name: "Microsoft.CodeAnalysis.*"
```

Source: [.NET compiler platform package version reference](https://learn.microsoft.com/en-us/visualstudio/extensibility/roslyn-version-support?view=visualstudio)

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

### LSG017

**Pipeline callbacks must be static**

Incremental pipeline callbacks run often, so they should be `static` whenever they do not capture enclosing locals, parameters, or instance state. Marking them `static` prevents accidental captures from creeping into hot paths later.

```csharp
// ❌ LSG017
var projected = values.Select((item, ct) => item.ToString());

// ✅ OK
var projected = values.Select(static (item, ct) => item.ToString());
```

---

### LSG018

**Prefer SelectMany over materialized collections in the pipeline**

Do not materialize arrays, `List<T>`, or `ImmutableArray<T>` in one stage only to flatten or iterate them immediately in the next stage. Prefer `SelectMany` so the pipeline keeps item granularity and avoids unnecessary collection churn.

```csharp
// ❌ LSG018
var materialized = values.Select(static (item, ct) => ImmutableArray.Create(item, item + 1));
var flattened = materialized.SelectMany(static (items, ct) => items);

// ✅ OK
var flattened = values.SelectMany(static (item, ct) => ImmutableArray.Create(item, item + 1));
```

---

### LSG019

**Delay Collect**

`Collect()` should happen only near the point where whole-set aggregation is actually required. If the next stage still performs item-level filtering or projection, keep those operations upstream and collect later.

```csharp
// ❌ LSG019
var collected = values.Collect();
var filtered = collected.Select(
    static (items, ct) => items.Where(static item => item.IsValid).ToImmutableArray());

// ✅ OK
var filtered = values.Where(static (item, ct) => item.IsValid);
var collected = filtered.Collect();
```

---

### LSG020

**Nested tuple proliferation in pipeline composition**

Repeated `Left` / `Right` tuple navigation and nested tuple construction quickly make `Combine`-heavy pipelines unreadable. Chaining `Combine` repeatedly until the callback input itself becomes a nested tuple such as `((Foo Left, Bar Right) Left, Baz Right)` is also reported. Flatten the shape or introduce a named intermediate model before the nesting spreads further. When matching `Left` / `Right` branches share the same collected type, merge them first instead of carrying both branches forward as nested tuples.

```csharp
// ❌ LSG020
var projected = combined.Select(
    static (value, ct) => ((value.Left.Left, value.Left.Right), value.Right));

// ✅ OK
var projected = combined.Select(
    static (value, ct) => new MyModel(value.Left.Left, value.Left.Right, value.Right));
```

When the repeated branches are the same collected type, a helper like this keeps the pipeline flat and deterministic:

```csharp
private static IncrementalValueProvider<EquatableArray<T>> MergeCollectedValues<T>(
    IncrementalValueProvider<ImmutableArray<T>> first,
    IncrementalValueProvider<ImmutableArray<T>> second
)
    where T : IEquatable<T>
{
    return first
        .Combine(second)
        .Select(static (pair, _) => new EquatableArray<T>(pair.Left.Concat(pair.Right)));
}
```

## License

```
Copyright 2026 arika0093

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0
```
