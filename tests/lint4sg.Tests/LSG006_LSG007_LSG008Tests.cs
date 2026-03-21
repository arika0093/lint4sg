using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
#pragma warning disable CS0618
using Microsoft.CodeAnalysis.Testing.Verifiers;
#pragma warning restore CS0618
using Xunit;

namespace lint4sg.Tests;

/// <summary>
/// Tests for LSG006, LSG007, LSG008 — DeterministicValueAnalyzer.
/// </summary>
public class LSG006_LSG007_LSG008_DeterministicValueTests
{
    // Stubs that expose IncrementalValueProvider and the register methods.
    private const string IncrementalStubs = """
        namespace Microsoft.CodeAnalysis
        {
            public interface ISymbol { }
            public abstract class SemanticModel { }
            public abstract class Compilation { }
            public abstract class SyntaxNode { }

            public struct IncrementalValueProvider<T> { }
            public struct IncrementalValuesProvider<T> { }

            public static class IncrementalProviderExtensions
            {
                public static IncrementalValuesProvider<TResult> Select<TSource, TResult>(
                    this IncrementalValuesProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, TResult> transform)
                    => default;
            }

            public class IncrementalGeneratorInitializationContext
            {
                public void RegisterSourceOutput<T>(
                    IncrementalValueProvider<T> source,
                    System.Action<object, T> action) { }

                public void RegisterImplementationSourceOutput<T>(
                    IncrementalValueProvider<T> source,
                    System.Action<object, T> action) { }
            }

            public class SyntaxValueProvider
            {
                public IncrementalValuesProvider<T> CreateSyntaxProvider<T>(
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, T> transform)
                    => default;

                public IncrementalValuesProvider<T> ForAttributeWithMetadataName<T>(
                    string fullyQualifiedMetadataName,
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, T> transform)
                    => default;
            }
        }
        """;

    // Needed for `record` types on netstandard2.0 targets in tests
    private const string IsExternalInitStub = """
        namespace System.Runtime.CompilerServices
        {
            internal static class IsExternalInit { }
        }
        """;

    private static Task RunTestAsync(string code, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<DeterministicValueAnalyzer, XUnitVerifier>
        {
            TestState = { Sources = { code, IncrementalStubs, IsExternalInitStub } },
        };
        test.TestState.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    // ── LSG006: direct non-deterministic types ────────────────────────────

    [Fact]
    public async Task ISymbol_DirectToRegisterSourceOutput_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<ISymbol> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, sym) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(9, 9, 9, 62)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task SemanticModel_DirectToRegisterSourceOutput_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<SemanticModel> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, sm) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(9, 9, 9, 61)
                .WithArguments("Microsoft.CodeAnalysis.SemanticModel")
        );
    }

    [Fact]
    public async Task Compilation_DirectToRegisterSourceOutput_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<Compilation> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, comp) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(9, 9, 9, 63)
                .WithArguments("Microsoft.CodeAnalysis.Compilation")
        );
    }

    [Fact]
    public async Task UserDefinedMutableClass_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyData
            {
                public string Name { get; set; } = "";
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<MyData> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, d) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(14, 9, 14, 60)
                .WithArguments("MyData")
        );
    }

    [Fact]
    public async Task ReferenceTypeWithValueEqualityAndDeterministicMembers_NoLSG006()
    {
        var code = """
            using System;
            using Microsoft.CodeAnalysis;

            public sealed class StableData : IEquatable<StableData>
            {
                public string Name { get; }
                public int Count { get; }

                public StableData(string name, int count)
                {
                    Name = name;
                    Count = count;
                }

                public bool Equals(StableData? other) => other is not null && Name == other.Name && Count == other.Count;
                public override bool Equals(object? obj) => obj is StableData other && Equals(other);
                public override int GetHashCode() => HashCode.Combine(Name, Count);
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<StableData> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, data) => { });
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task ReferenceTypeWithValueEqualityButISymbolMember_ReportsLSG006()
    {
        var code = """
            using System;
            using Microsoft.CodeAnalysis;

            public sealed class StableData : IEquatable<StableData>
            {
                public ISymbol Symbol { get; }

                public StableData(ISymbol symbol)
                {
                    Symbol = symbol;
                }

                public bool Equals(StableData? other) => ReferenceEquals(Symbol, other?.Symbol);
                public override bool Equals(object? obj) => obj is StableData other && Equals(other);
                public override int GetHashCode() => 0;
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<StableData> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, data) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(24, 9, 24, 63)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task ReferenceTypeWithValueEqualityButPrivateISymbolField_ReportsLSG006()
    {
        var code = """
            using System;
            using Microsoft.CodeAnalysis;

            public sealed class StableData : IEquatable<StableData>
            {
                private readonly ISymbol _symbol;

                public StableData(ISymbol symbol)
                {
                    _symbol = symbol;
                }

                public bool Equals(StableData? other) => ReferenceEquals(_symbol, other?._symbol);
                public override bool Equals(object? obj) => obj is StableData other && Equals(other);
                public override int GetHashCode() => 0;
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<StableData> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, data) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(24, 9, 24, 63)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    // ── LSG006: non-deterministic type inside record (child / grandchild) ─

    [Fact]
    public async Task RecordWithISymbolProperty_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public record MyInfo(string Name, ISymbol Symbol);

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<MyInfo> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, info) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(11, 9, 11, 63)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task RecordWithCompilationNestedTwoLevels_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public record Inner(string Text, Compilation Comp);
            public record Outer(int Id, Inner Data);

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<Outer> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, o) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(12, 9, 12, 60)
                .WithArguments("Microsoft.CodeAnalysis.Compilation")
        );
    }

    [Fact]
    public async Task RecordWithOnlyEquatableMembers_NoLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public record MyInfo(string Name, int Count);

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<MyInfo> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, info) => { });
                }
            }
            """;

        await RunTestAsync(code);
    }

    // ── LSG007: array / collection type ──────────────────────────────────

    [Fact]
    public async Task ArrayType_ReportsLSG007()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<string[]> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, arr) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG007", DiagnosticSeverity.Error)
                .WithSpan(9, 9, 9, 62)
                .WithArguments("string[]")
        );
    }

    [Fact]
    public async Task ListType_ReportsLSG007()
    {
        var code = """
            using System.Collections.Generic;
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<List<string>> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, list) => { });
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG007", DiagnosticSeverity.Error)
                .WithSpan(10, 9, 10, 63)
                .WithArguments("System.Collections.Generic.List<string>")
        );
    }

    [Fact]
    public async Task ValueEqualityCollectionWrapper_NoLSG007()
    {
        var code = """
            using System.Collections;
            using System.Collections.Generic;
            using Microsoft.CodeAnalysis;

            public sealed class PathSegments : IReadOnlyList<string>, System.IEquatable<PathSegments>
            {
                public int Count => 0;
                public string this[int index] => "";

                public bool Equals(PathSegments? other) => other is not null;
                public override bool Equals(object? obj) => obj is PathSegments other && Equals(other);
                public override int GetHashCode() => 0;

                public IEnumerator<string> GetEnumerator()
                {
                    yield break;
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<PathSegments> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, segments) => { });
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task RecordWithEquatableImmutableArrayWrapper_NoLSG007()
    {
        var code = """
            using System.Collections;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Linq;
            using Microsoft.CodeAnalysis;

            public sealed class EquatableArray<T> : IReadOnlyList<T>, System.IEquatable<EquatableArray<T>>
            {
                private readonly ImmutableArray<T> _items;

                public EquatableArray(ImmutableArray<T> items)
                {
                    _items = items;
                }

                public int Count => _items.Length;
                public T this[int index] => _items[index];

                public bool Equals(EquatableArray<T>? other) => other is not null && _items.SequenceEqual(other._items);
                public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);
                public override int GetHashCode() => Count;

                public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public record GeneratedSourceSetModel(EquatableArray<string> Sources);

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<GeneratedSourceSetModel> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, model) => { });
                }
            }
            """;

        await RunTestAsync(code);
    }

    // ── LSG008: SyntaxProvider returns non-deterministic type (warning) ───

    [Fact]
    public async Task SyntaxProvider_ISymbolReturn_ReportsLSG008()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(SyntaxValueProvider provider)
                {
                    var result = provider.CreateSyntaxProvider<ISymbol>(
                        (node, ct) => true,
                        (ctx, ct) => null!);
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG008", DiagnosticSeverity.Warning)
                .WithSpan(7, 22, 9, 32)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task SyntaxProvider_RecordWithSemanticModelChild_ReportsLSG008()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public record MyInfo(string Name, SemanticModel Sem);

            public class MyGenerator
            {
                public void Run(SyntaxValueProvider provider)
                {
                    var result = provider.CreateSyntaxProvider<MyInfo>(
                        (node, ct) => true,
                        (ctx, ct) => null!);
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG008", DiagnosticSeverity.Warning)
                .WithSpan(9, 22, 11, 32)
                .WithArguments("Microsoft.CodeAnalysis.SemanticModel")
        );
    }

    [Fact]
    public async Task SyntaxProvider_ValueEqualityTypeWithPrivateISymbolField_ReportsLSG008()
    {
        var code = """
            using System;
            using Microsoft.CodeAnalysis;

            public sealed class StableData : IEquatable<StableData>
            {
                private readonly ISymbol _symbol;

                public StableData(ISymbol symbol)
                {
                    _symbol = symbol;
                }

                public bool Equals(StableData? other) => ReferenceEquals(_symbol, other?._symbol);
                public override bool Equals(object? obj) => obj is StableData other && Equals(other);
                public override int GetHashCode() => 0;
            }

            public class MyGenerator
            {
                public void Run(SyntaxValueProvider provider)
                {
                    var result = provider.CreateSyntaxProvider<StableData>(
                        (node, ct) => true,
                        (ctx, ct) => null!);
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG008", DiagnosticSeverity.Warning)
                .WithSpan(22, 22, 24, 32)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task SyntaxProvider_RecordWithEquatableMembers_NoWarning()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public record MyInfo(string Name, int Count);

            public class MyGenerator
            {
                public void Run(SyntaxValueProvider provider)
                {
                    var result = provider.CreateSyntaxProvider<MyInfo>(
                        (node, ct) => true,
                        (ctx, ct) => null!);
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task SyntaxProvider_RecordWithEquatableImmutableArrayWrapper_NoWarning()
    {
        var code = """
            using System.Collections;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Linq;
            using Microsoft.CodeAnalysis;

            public sealed class EquatableArray<T> : IReadOnlyList<T>, System.IEquatable<EquatableArray<T>>
            {
                private readonly ImmutableArray<T> _items;

                public EquatableArray(ImmutableArray<T> items)
                {
                    _items = items;
                }

                public int Count => _items.Length;
                public T this[int index] => _items[index];

                public bool Equals(EquatableArray<T>? other) => other is not null && _items.SequenceEqual(other._items);
                public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);
                public override int GetHashCode() => Count;

                public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public record GeneratedSourceSetModel(EquatableArray<string> Sources);

            public class MyGenerator
            {
                public void Run(SyntaxValueProvider provider)
                {
                    var result = provider.CreateSyntaxProvider<GeneratedSourceSetModel>(
                        (node, ct) => true,
                        (ctx, ct) => null!);
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task SyntaxProvider_RecordWithEquatableImmutableArrayStructWrapper_NoWarning()
    {
        var code = """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Linq;
            using Microsoft.CodeAnalysis;

            public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
                where T : IEquatable<T>
            {
                private readonly ImmutableArray<T> _items;

                public EquatableArray(ImmutableArray<T> items)
                {
                    _items = items;
                }

                public int Count => _items.Length;
                public T this[int index] => _items[index];

                public bool Equals(EquatableArray<T> other) => _items.SequenceEqual(other._items);
                public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);
                public override int GetHashCode() => Count;

                public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public record GeneratedSourceSetModel(EquatableArray<string> Sources);

            public class MyGenerator
            {
                public void Run(SyntaxValueProvider provider)
                {
                    var result = provider.CreateSyntaxProvider<GeneratedSourceSetModel>(
                        (node, ct) => true,
                        (ctx, ct) => null!);
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task SyntaxProvider_RecordWithEquatableImmutableArrayStructWrapperOfSymbols_ReportsLSG008()
    {
        var code = """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Linq;
            using Microsoft.CodeAnalysis;

            public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
            {
                private readonly ImmutableArray<T> _items;

                public EquatableArray(ImmutableArray<T> items)
                {
                    _items = items;
                }

                public int Count => _items.Length;
                public T this[int index] => _items[index];

                public bool Equals(EquatableArray<T> other) => _items.SequenceEqual(other._items);
                public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);
                public override int GetHashCode() => Count;

                public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public record GeneratedSourceSetModel(EquatableArray<ISymbol> Sources);

            public class MyGenerator
            {
                public void Run(SyntaxValueProvider provider)
                {
                    var result = provider.CreateSyntaxProvider<GeneratedSourceSetModel>(
                        (node, ct) => true,
                        (ctx, ct) => null!);
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG008", DiagnosticSeverity.Warning)
                .WithSpan(33, 22, 35, 32)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task SyntaxProvider_ForAttributeWithMetadataName_SelectOverImmutableArrayOfSymbols_ReportsLSG008OnElementType()
    {
        var code = """
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(SyntaxValueProvider provider)
                {
                    var result = provider.ForAttributeWithMetadataName<ImmutableArray<ISymbol>>(
                        "MyAttribute",
                        (node, ct) => true,
                        (ctx, ct) => default)
                        .Select((x, ct) => x);
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG008", DiagnosticSeverity.Warning)
                .WithSpan(8, 22, 11, 34)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task SyntaxProvider_CreateSyntaxProvider_ImmutableArrayOfStrings_NoWarning()
    {
        var code = """
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(SyntaxValueProvider provider)
                {
                    var result = provider.CreateSyntaxProvider<ImmutableArray<string>>(
                        (node, ct) => true,
                        (ctx, ct) => default);
                }
            }
            """;

        await RunTestAsync(code);
    }
}
