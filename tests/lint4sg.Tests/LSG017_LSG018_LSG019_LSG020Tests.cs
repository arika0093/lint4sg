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

public class LSG017_LSG018_LSG019_LSG020_PipelineTests
{
    private const string GenericTupleGuidance =
        "Flatten the model or introduce a named type.";
    private const string SameTypeTupleMergeGuidance =
        "Because matching Left and Right branches have the same type, merge them first with a helper such as MergeCollectedValues<T>(first, second).";

    private const string IncrementalStubs = """
        namespace Microsoft.CodeAnalysis
        {
            public interface IIncrementalGenerator
            {
                void Initialize(IncrementalGeneratorInitializationContext context);
            }

            public readonly struct IncrementalValueProvider<T> { }
            public readonly struct IncrementalValuesProvider<T> { }

            public sealed class SyntaxValueProvider
            {
                public IncrementalValuesProvider<TResult> CreateSyntaxProvider<TResult>(
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, TResult> transform)
                    => default;

                public IncrementalValuesProvider<TResult> ForAttributeWithMetadataName<TResult>(
                    string fullyQualifiedMetadataName,
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, TResult> transform)
                    => default;
            }

            public sealed class IncrementalGeneratorInitializationContext
            {
                public SyntaxValueProvider SyntaxProvider => null!;

                public void RegisterSourceOutput<TSource>(
                    IncrementalValueProvider<TSource> source,
                    System.Action<object, TSource> action) { }

                public void RegisterImplementationSourceOutput<TSource>(
                    IncrementalValueProvider<TSource> source,
                    System.Action<object, TSource> action) { }
            }

            public static class IncrementalProviderExtensions
            {
                public static IncrementalValuesProvider<TResult> Select<TSource, TResult>(
                    this IncrementalValuesProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, TResult> transform)
                    => default;

                public static IncrementalValueProvider<TResult> Select<TSource, TResult>(
                    this IncrementalValueProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, TResult> transform)
                    => default;

                public static IncrementalValuesProvider<TSource> Where<TSource>(
                    this IncrementalValuesProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, bool> predicate)
                    => default;

                public static IncrementalValueProvider<System.Collections.Immutable.ImmutableArray<TSource>> Collect<TSource>(
                    this IncrementalValuesProvider<TSource> source)
                    => default;

                public static IncrementalValuesProvider<TResult> SelectMany<TSource, TResult>(
                    this IncrementalValuesProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, System.Collections.Immutable.ImmutableArray<TResult>> selector)
                    => default;

                public static IncrementalValueProvider<(TLeft Left, TRight Right)> Combine<TLeft, TRight>(
                    this IncrementalValueProvider<TLeft> left,
                    IncrementalValueProvider<TRight> right)
                    => default;
            }
        }
        """;

    private static Task RunTestAsync(string code, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<IncrementalPipelineAnalyzer, XUnitVerifier>
        {
            TestState = { Sources = { code, IncrementalStubs } },
        };
        test.TestState.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    [Fact]
    public async Task Select_CallbackWithoutCapture_ReportsLSG017()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var projected = values.Select({|#0:(item, ct) => item.ToString()|});
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG017", DiagnosticSeverity.Error).WithLocation(0)
        );
    }

    [Fact]
    public async Task CreateSyntaxProvider_CallbacksWithoutCapture_ReportLSG017()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    var values = context.SyntaxProvider.CreateSyntaxProvider(
                        {|#0:(node, ct) => node is object|},
                        {|#1:(ctx, ct) => ctx|});
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG017", DiagnosticSeverity.Error).WithLocation(0),
            new DiagnosticResult("LSG017", DiagnosticSeverity.Error).WithLocation(1)
        );
    }

    [Fact]
    public async Task Select_CallbackThatCapturesLocal_DoesNotReportLSG017()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var suffix = "_generated";
                    var projected = values.Select((item, ct) => item.ToString() + suffix);
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task Select_StaticCallback_DoesNotReportLSG017()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var projected = values.Select(static (item, ct) => item.ToString());
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task SelectManyAfterMaterializingCollection_ReportsLSG018()
    {
        var code = """
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var materialized = {|#0:values.Select(static (item, ct) => ImmutableArray.Create(item, item + 1))|};
                    var flattened = materialized.SelectMany(static (items, ct) => items);
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG018", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithArguments("System.Collections.Immutable.ImmutableArray<int>")
        );
    }

    [Fact]
    public async Task MaterializedCollectionUsedForAggregate_DoesNotReportLSG018()
    {
        var code = """
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var materialized = values.Select(static (item, ct) => ImmutableArray.Create(item, item + 1));
                    var summary = materialized.Select(static (items, ct) => items.Length);
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task CollectBeforeItemLevelProjection_ReportsLSG019()
    {
        var code = """
            using System.Collections.Immutable;
            using System.Linq;
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var collected = {|#0:values.Collect()|};
                    var filtered = collected.Select(
                        static (items, ct) => items.Where(static item => item > 0).ToImmutableArray());
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG019", DiagnosticSeverity.Error).WithLocation(0)
        );
    }

    [Fact]
    public async Task CollectUsedForWholeSetAggregation_DoesNotReportLSG019()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var collected = values.Collect();
                    context.RegisterSourceOutput(collected, static (spc, items) =>
                    {
                        _ = items.Length;
                    });
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task NestedTupleProjection_WithSameTypeLeftRight_ReportsMergeGuidance()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValueProvider<int> left = default;
                    IncrementalValueProvider<int> right = default;
                    IncrementalValueProvider<int> third = default;
                    var combined = left.Combine(right).Combine(third);
                    var projected = combined.Select(static (value, ct) => {|#0:((value.Left.Left, value.Left.Right), value.Right)|});
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG020", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithArguments(SameTypeTupleMergeGuidance)
        );
    }

    [Fact]
    public async Task NestedTupleProjection_WithDifferentLeftRightTypes_ReportsGenericGuidance()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValueProvider<string> left = default;
                    IncrementalValueProvider<int> right = default;
                    IncrementalValueProvider<bool> third = default;
                    var combined = left.Combine(right).Combine(third);
                    var projected = combined.Select(static (value, ct) => {|#0:((value.Left.Left, value.Left.Right), value.Right)|});
                }
            }
            """;

        await RunTestAsync(
            code,
            new DiagnosticResult("LSG020", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithArguments(GenericTupleGuidance)
        );
    }

    [Fact]
    public async Task MergeCollectedValuesStyleHelper_DoesNotReportLSG020()
    {
        var code = """
            using System;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Linq;
            using Microsoft.CodeAnalysis;

            public readonly struct EquatableArray<T>
                where T : IEquatable<T>
            {
                public EquatableArray(IEnumerable<T> values)
                {
                }
            }

            public static class PipelineHelpers
            {
                private static IncrementalValueProvider<EquatableArray<T>> MergeCollectedValues<T>(
                    IncrementalValueProvider<ImmutableArray<T>> first,
                    IncrementalValueProvider<ImmutableArray<T>> second)
                    where T : IEquatable<T>
                {
                    return first
                        .Combine(second)
                        .Select(static (pair, _) => new EquatableArray<T>(pair.Left.Concat(pair.Right)));
                }
            }
            """;

        await RunTestAsync(code);
    }

    [Fact]
    public async Task SimpleTupleProjection_DoesNotReportLSG020()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValueProvider<int> left = default;
                    IncrementalValueProvider<int> right = default;
                    var combined = left.Combine(right);
                    var projected = combined.Select(static (value, ct) => (value.Left, value.Right));
                }
            }
            """;

        await RunTestAsync(code);
    }
}
