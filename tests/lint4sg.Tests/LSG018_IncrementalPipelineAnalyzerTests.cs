using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG018_IncrementalPipelineAnalyzerTests
{
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
    }
}
