using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG002_SyntaxProviderUsageAnalyzerTests
{
    [Fact]
    public async Task CreateSyntaxProvider_ReportsLSG002()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => node is object,
                        (ctx, ct) => ctx);
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG002",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
        ).WithSpan(8, 22, 10, 30);

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task CreateSyntaxProvider_WithIntentionalInvocationMarker_DoesNotReportLSG002()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    // lint4sg-allow-create-syntax-provider: invocation-shape matching is intentional here
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => node is MyInvocationSyntax invocation && invocation.TargetName == "SelectExpr",
                        (ctx, ct) => ctx);
                }
            }

            public sealed class MyInvocationSyntax
            {
                public string TargetName => "";
            }
            """;

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task CreateSyntaxProvider_WithIntentionalEmptyProviderMarker_DoesNotReportLSG002()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    // lint4sg-allow-create-syntax-provider: intentionally returning an empty provider
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => false,
                        (ctx, ct) => ctx);
                }
            }
            """;

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task CreateSyntaxProvider_WithIntentionalTrailingMarker_DoesNotReportLSG002()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => node is object,
                        (ctx, ct) => ctx); // lint4sg-allow-create-syntax-provider
                }
            }
            """;

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task ForAttributeWithMetadataName_NoWarnings()
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
                        (node, ct) => true,
                        (ctx, ct) => ctx);
                }
            }
            """;

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code);
        await test.RunAsync();
    }
}
