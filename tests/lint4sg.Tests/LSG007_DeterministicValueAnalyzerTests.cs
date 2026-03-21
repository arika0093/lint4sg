using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG007_DeterministicValueAnalyzerTests
{
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task CollectionLikeStructWithoutExplicitValueEquality_ReportsLSG007()
    {
        var code = """
            using System.Collections;
            using System.Collections.Generic;
            using Microsoft.CodeAnalysis;

            public struct Segments : IEnumerable<string>
            {
                private readonly List<string> _items;

                public IEnumerator<string> GetEnumerator() => (_items ?? new List<string>()).GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<Segments> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, segments) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG007", DiagnosticSeverity.Error)
                .WithSpan(19, 9, 19, 67)
                .WithArguments("System.Collections.Generic.List<string>")
        );
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

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
    }
}
