using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG016_SyntaxProviderUsageAnalyzerTests
{
    [Fact]
    public async Task CreateSyntaxProvider_PredicateAllocation_ReportsLSG016()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => new object() != null,
                        (ctx, ct) => ctx);
                }
            }
            """;

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(
            code,
            new DiagnosticResult(
                "LSG002",
                Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
            ).WithSpan(8, 22, 10, 30),
            new DiagnosticResult(
                "LSG016",
                Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            ).WithSpan(9, 27, 9, 39)
        );

        await test.RunAsync();
    }

    [Fact]
    public async Task ForAttributeWithMetadataName_PredicateAllocation_ReportsLSG016()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    var result = provider.ForAttributeWithMetadataName(
                        "MyAttribute",
                        (node, ct) => new[] { node }.Length > 0,
                        (ctx, ct) => ctx);
                }
            }
            """;

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(
            code,
            new DiagnosticResult(
                "LSG016",
                Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            ).WithSpan(10, 27, 10, 41)
        );

        await test.RunAsync();
    }
}
