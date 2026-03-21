using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG017_IncrementalPipelineAnalyzerTests
{
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
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

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task Select_NonStaticCallbackReferencingLocalConst_ReportsLSG017()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    const string suffix = ".g.cs";
                    var projected = values.Select({|#0:(item, ct) => item.ToString() + suffix|});
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG017", DiagnosticSeverity.Error).WithLocation(0)
        );
    }

    [Fact]
    public async Task Select_StaticCallbackReferencingLocalConst_DoesNotReportLSG017()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    const string suffix = ".g.cs";
                    var projected = values.Select(static (item, ct) => item.ToString() + suffix);
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task Select_InstanceMethodGroup_ReportsLSG017()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                private string Project(int item, System.Threading.CancellationToken ct)
                    => item.ToString();

                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var projected = values.Select({|#0:Project|});
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG017", DiagnosticSeverity.Error).WithLocation(0)
        );
    }

    [Fact]
    public async Task Select_StaticMethodGroup_DoesNotReportLSG017()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public sealed class MyGenerator : IIncrementalGenerator
            {
                private static string Project(int item, System.Threading.CancellationToken ct)
                    => item.ToString();

                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    IncrementalValuesProvider<int> values = default;
                    var projected = values.Select(Project);
                }
            }
            """;

        await IncrementalPipelineAnalyzerTestHelpers.RunTestAsync(code);
    }
}
