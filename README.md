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

### Errors — SourceGenerator category

| ID | Title | Description |
|----|-------|-------------|
| [LSG001](#lsg001) | Avoid ISourceGenerator | Use `IIncrementalGenerator` instead |
| [LSG003](#lsg003) | Avoid high-cost SyntaxProvider predicate | Inheritance checks in predicates are expensive |
| [LSG004](#lsg004) | Forward CancellationToken | CT must be forwarded to all accepting callees |
| [LSG005](#lsg005) | Missing ThrowIfCancellationRequested in loop | Loops must check CT on each iteration |
| [LSG006](#lsg006) | Non-deterministic value in RegisterSourceOutput | Non-equatable types defeat Roslyn caching |
| [LSG007](#lsg007) | Non-deterministic collection in RegisterSourceOutput | Arrays/lists use reference equality |
| [LSG009](#lsg009) | Avoid NormalizeWhitespace | O(n) syntax-tree traversal — use a writer instead |
| [LSG010](#lsg010) | Excessive whitespace in AppendLine | Use indentation-aware StringBuilder |
| [LSG011](#lsg011) | Use raw string literal | Replace 3+ consecutive AppendLine calls |

### Warnings — SourceGenerator category

| ID | Title | Description |
|----|-------|-------------|
| [LSG002](#lsg002) | Prefer ForAttributeWithMetadataName | `FAWMN` is heavily optimised over `CreateSyntaxProvider` |
| [LSG008](#lsg008) | Non-deterministic SyntaxProvider return value | May cause unnecessary pipeline re-execution |
| [LSG012](#lsg012) | External dependency in source generator | Complicates NuGet packaging |
| [LSG013](#lsg013) | Avoid Reflection API in source generator | Defeats compile-time code generation benefits |
| [LSG014](#lsg014) | CodeAnalysis.CSharp version may be too new | Lower versions support more environments |

### Info — Performance category

| ID | Title | Description |
|----|-------|-------------|
| [LSG101](#lsg101) | Consider `in` modifier for struct parameter | Avoids struct copies on method calls |
| [LSG102](#lsg102) | Consider interpolated string | `string.Format` → `$"..."` |
| [LSG103](#lsg103) | Use StringBuilder in loops | Avoids repeated string allocations |

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

---

### LSG003

**Avoid high-cost SyntaxProvider predicate**

Inheritance checks (`.Interfaces`, `.BaseType`, `IsAssignableTo`, etc.) inside a `SyntaxProvider` predicate run on every syntax change for every node. They require symbol resolution which is expensive. Move such checks to the `transform` step, or use `ForAttributeWithMetadataName` to pre-filter.

```csharp
// ❌ LSG003
var result = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: (node, ct) => node.GetType().BaseType != null, // expensive!
    transform: (ctx, ct) => ctx.Node);

// ✅ OK — semantic checks belong in the transform step
var result = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: (node, ct) => node is ClassDeclarationSyntax,
    transform: (ctx, ct) => {
        var cls = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(cls, ct);
        return symbol?.BaseType?.Name == "MyBase" ? symbol : null;
    });
```

---

### LSG004

**Forward CancellationToken to all accepting callees**

Incremental generators receive a `CancellationToken` that should be forwarded everywhere. If a called method accepts a `CancellationToken` parameter, pass the token so that cancellation is propagated correctly.

```csharp
// ❌ LSG004
void Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
{
    DoWork(); // DoWork(CancellationToken) overload exists!
}

// ✅ OK
void Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
{
    DoWork(ct);
}
```

---

### LSG005

**Call ThrowIfCancellationRequested() in each loop iteration**

When a `CancellationToken` is in scope, every loop must call `ct.ThrowIfCancellationRequested()` to ensure the generator responds promptly to cancellation. Without this, a long-running loop can block the IDE.

```csharp
// ❌ LSG005
void Process(IEnumerable<ISymbol> symbols, CancellationToken ct)
{
    foreach (var symbol in symbols) // missing ThrowIfCancellationRequested!
    {
        Generate(symbol);
    }
}

// ✅ OK
void Process(IEnumerable<ISymbol> symbols, CancellationToken ct)
{
    foreach (var symbol in symbols)
    {
        ct.ThrowIfCancellationRequested();
        Generate(symbol);
    }
}
```

---

### LSG006

**Non-deterministic value in RegisterSourceOutput**

`RegisterSourceOutput` caches results based on value equality of the input. Types like `ISymbol`, `SyntaxNode`, `SemanticModel`, `Compilation`, and plain mutable classes use reference equality, so the cache will never hit and the generator will re-run on every compilation. This also applies to **nested** members — if a record contains an `ISymbol` field, the whole record is non-deterministic.

Use records, structs, or primitives whose members are all equatable.

```csharp
// ❌ LSG006 — ISymbol inside record defeats caching
public record MyInfo(string Name, ISymbol Symbol);
context.RegisterSourceOutput(provider, (spc, info) => Generate(spc, info));

// ✅ OK — extract only the data you need
public record MyInfo(string Name, string FullName);
context.RegisterSourceOutput(provider, (spc, info) => Generate(spc, info));
```

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

---

### LSG008

**Non-deterministic SyntaxProvider return value** *(warning)*

The same equatability requirements from LSG006/LSG007 apply to the values returned by `ForAttributeWithMetadataName` and `CreateSyntaxProvider` transforms. If the transform returns a non-equatable type, Roslyn's caching will not work. This is a warning rather than an error because some unavoidable patterns exist.

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

---

### LSG010

**Excessive whitespace in AppendLine**

Manually indenting code with 8 or more consecutive spaces (or 2+ tabs) in `AppendLine` is fragile and hard to maintain. Use an indentation-aware `StringBuilder` utility.

```csharp
// ❌ LSG010
sb.AppendLine("        public void Method()");

// ✅ OK — use IndentedStringBuilder or IndentedTextWriter
sb.AppendLine("public void Method()");
```

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

---

### LSG013

**Avoid Reflection API in source generator** *(warning)*

Using `System.Reflection` in a source generator (or generating code that uses reflection) defeats the purpose of compile-time code generation. Source generators should produce static code. When an LLM generates reflection-based code in a generator, this rule fires immediately to draw attention.

---

### LSG014

**Microsoft.CodeAnalysis.CSharp version may be too new** *(warning)*

A source generator that references a newer version of `Microsoft.CodeAnalysis.CSharp` will only run in IDEs and build tools that ship that version. Using a lower version (< 5.0.0 as of March 2026) maximises compatibility with older Visual Studio / Rider installations and CI agents.

---

### LSG101

**Consider `in` modifier for large struct parameters** *(info)*

Passing large structs by value causes a copy on every call. Adding the `in` modifier passes by reference without allowing mutation.

```csharp
// Before
public void Process(LargeStruct data) { ... }

// After
public void Process(in LargeStruct data) { ... }
```

---

### LSG102

**Consider interpolated string instead of string.Format** *(info)*

Interpolated strings can use custom `InterpolatedStringHandler` implementations for zero-allocation formatting.

```csharp
// Before
var msg = string.Format("Hello, {0}!", name);

// After
var msg = $"Hello, {name}!";
```

---

### LSG103

**Use StringBuilder for string concatenation in loops** *(info)*

`string` is immutable; each `+` operation allocates a new string. Inside a loop, this creates many short-lived allocations.

```csharp
// Before
var result = "";
foreach (var item in items)
    result += item;

// After
var sb = new StringBuilder();
foreach (var item in items)
    sb.Append(item);
var result = sb.ToString();
```

---

## References

- [Roslyn Incremental Generators design document](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)
- [Incremental Generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)
- [Avoiding performance pitfalls in incremental generators](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/)
- [C# Advanced Performance topics](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/)

## License

This project is licensed under the MIT License.
