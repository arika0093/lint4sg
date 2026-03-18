using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using lint4sg.Analyzers;

namespace lint4sg.Tests;

public class LSG002_LSG003_SyntaxProviderTests
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

        // LSG002 fires for CreateSyntaxProvider usage
        var expected = new DiagnosticResult("LSG002", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(8, 22, 10, 30);

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task CreateSyntaxProvider_WithInheritanceCheck_ReportsLSG003()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => node.GetType().BaseType != null,
                        (ctx, ct) => ctx);
                }
            }
            """;

        // Both LSG002 (warning) and LSG003 (error for BaseType check in predicate)
        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code,
            new DiagnosticResult("LSG002", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(8, 22, 10, 30),
            new DiagnosticResult("LSG003", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(9, 13, 9, 58));

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

    [Fact]
    public async Task CreateSyntaxProvider_TransformWithGetDeclaredSymbolInheritanceScan_ReportsLSG003()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    var semanticModel = new MySemanticModel();
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => node is object,
                        (ctx, ct) => ((MySymbol)semanticModel.GetDeclaredSymbol(ctx, ct)).BaseType);
                }
            }

            public sealed class MySemanticModel : SemanticModel { }

            public sealed class MySymbol : ISymbol
            {
                public object BaseType => null!;
            }
            """;

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code,
            new DiagnosticResult("LSG002", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(9, 22, 11, 88),
            new DiagnosticResult("LSG003", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(11, 13, 11, 87));

        await test.RunAsync();
    }

    [Fact]
    public async Task CreateSyntaxProvider_TransformWithGetDeclaredSymbolAfterPredicateNarrowing_DoesNotReportLSG003()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    var semanticModel = new MySemanticModel();
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => node is MyNode candidate && candidate.HasAttributes,
                        (ctx, ct) => ((MySymbol)semanticModel.GetDeclaredSymbol(ctx, ct)).BaseType);
                }
            }

            public sealed class MySemanticModel : SemanticModel { }

            public sealed class MyNode
            {
                public bool HasAttributes => true;
            }

            public sealed class MySymbol : ISymbol
            {
                public object BaseType => null!;
            }
            """;

        var expected = new DiagnosticResult("LSG002", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(9, 22, 11, 88);

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task CreateSyntaxProvider_TransformWithInheritanceCheckButNoGetDeclaredSymbol_DoesNotReportLSG003()
    {
        // LSG003 targets broad semantic scans through GetDeclaredSymbol.
        // A transform that does not do that should still only get LSG002.
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => node is object,
                        (ctx, ct) => ctx.GetType().BaseType);
                }
            }
            """;

        // Only LSG002 should be reported — NOT LSG003 for the transform
        var expected = new DiagnosticResult("LSG002", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(8, 22, 10, 49);

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task CreateSyntaxProvider_TransformWithGetDeclaredSymbolButNoInheritanceScan_DoesNotReportLSG003()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    var semanticModel = new MySemanticModel();
                    var result = provider.CreateSyntaxProvider(
                        (node, ct) => node is object,
                        (ctx, ct) => ((MySymbol)semanticModel.GetDeclaredSymbol(ctx, ct)).Name);
                }
            }

            public sealed class MySemanticModel : SemanticModel { }

            public sealed class MySymbol : ISymbol
            {
                public string Name => "";
            }
            """;

        var expected = new DiagnosticResult("LSG002", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(9, 22, 11, 84);

        var test = TestHelpers.CreateTest<SyntaxProviderUsageAnalyzer>(code, expected);
        await test.RunAsync();
    }
}
