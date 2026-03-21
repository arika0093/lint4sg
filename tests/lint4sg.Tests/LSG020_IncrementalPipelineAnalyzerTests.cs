using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG020_IncrementalPipelineAnalyzerTests
{
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG020", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithArguments(IncrementalPipelineAnalyzerTestHelpers.SameTypeTupleMergeGuidance)
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG020", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithArguments(IncrementalPipelineAnalyzerTestHelpers.GenericTupleGuidance)
        );
    }

    [Fact]
    public async Task ChainedCombine_WithSameTypeCollectedInputs_ReportsMergeGuidance()
    {
        var code = """
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> first = default;
                    IncrementalValuesProvider<int> second = default;
                    IncrementalValuesProvider<int> third = default;
                    var combined = first.Collect().Combine(second.Collect()).Combine(third.Collect());
                    var projected = combined.Select(static ({|#0:value|}, ct) => value.Right);
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG020", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithArguments(IncrementalPipelineAnalyzerTestHelpers.SameTypeTupleMergeGuidance)
        );
    }

    [Fact]
    public async Task ChainedCombine_WithDifferentCollectedInputs_ReportsGenericGuidance()
    {
        var code = """
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<string> first = default;
                    IncrementalValuesProvider<int> second = default;
                    IncrementalValuesProvider<bool> third = default;
                    var combined = first.Collect().Combine(second.Collect()).Combine(third.Collect());
                    var projected = combined.Select(static ({|#0:value|}, ct) => value.Right);
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG020", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithArguments(IncrementalPipelineAnalyzerTestHelpers.GenericTupleGuidance)
        );
    }

    [Fact]
    public async Task NestedTupleProviderInput_WithoutCombineChain_DoesNotReportLSG020()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValueProvider<((int X, int Y), string Label)> values = default;
                    var projected = values.Select(static (value, ct) => value.Label.Length > 0);
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task SingleCombine_WithExistingNestedTupleInput_DoesNotReportLSG020()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValueProvider<((int X, int Y), string Label)> left = default;
                    IncrementalValueProvider<bool> right = default;
                    var combined = left.Combine(right);
                    var projected = combined.Select(static (value, ct) => value.Right);
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task DomainModelLeftRightProperties_DoesNotReportLSG020()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class TreeNode
            {
                public TreeNode? Left { get; }
                public TreeNode? Right { get; }
            }

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<TreeNode> values = default;
                    var projected = values.Select(static (node, ct) => node.Left != null ? node.Left.Left : node.Right);
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
    }
}
