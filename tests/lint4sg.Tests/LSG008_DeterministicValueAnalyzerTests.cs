using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG008_DeterministicValueAnalyzerTests
{
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
    }
}
