using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG019_IncrementalPipelineAnalyzerTests
{
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task CollectFollowedByAggregationForeach_DoesNotReportLSG019()
    {
        var code = """
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var sum = values.Collect().Select(static (items, _) =>
                    {
                        var acc = 0;
                        foreach (var i in items)
                            acc += i;
                        return acc;
                    });
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task ChainedCollectThenSelect_ReportsLSG019OnCollect()
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
                    var result = {|#0:values.Collect()|}
                        .Select(static (items, ct) => items)
                        .Select(static (items, ct) =>
                            items.Where(static x => x > 0).ToImmutableArray());
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG019", DiagnosticSeverity.Error).WithLocation(0)
        );
    }

    [Fact]
    public async Task ChainedCollectThenWhere_ReportsLSG019OnCollect()
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
                    var filtered = collected.Where(static (arr, ct) => arr.Length > 0);
                    var result = filtered.Select(static (items, ct) =>
                        items.Where(static x => x > 0).ToImmutableArray());
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG019", DiagnosticSeverity.Error).WithLocation(0)
        );
    }
}
