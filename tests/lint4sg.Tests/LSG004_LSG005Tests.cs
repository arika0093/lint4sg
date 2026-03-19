using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using lint4sg.Analyzers;

namespace lint4sg.Tests;

public class LSG004_LSG005_CancellationTokenTests
{
    [Fact]
    public async Task SourceGeneratorCallTreeWithLoop_ReportsLSG004ForEveryMissingHelper()
    {
        var code = """
            using System.Collections.Generic;
            using Microsoft.CodeAnalysis;

            public class Generator
            {
                public void Initialize()
                {
                    var provider = new SyntaxValueProvider();
                    provider.CreateSyntaxProvider(
                        (node, ct) => true,
                        (ctx, ct) => Parse());
                }

                private object Parse()
                {
                    return Analyze();
                }

                private object Analyze()
                {
                    foreach (var item in new List<int>())
                    {
                    }

                    return null!;
                }
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code,
            new DiagnosticResult("LSG004", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(14, 20, 14, 25)
                .WithArguments("Parse"),
            new DiagnosticResult("LSG004", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(19, 20, 19, 27)
                .WithArguments("Analyze"));

        await test.RunAsync();
    }

    [Fact]
    public async Task SourceGeneratorCallTreeWithExternalCtOverload_ReportsLSG004ForMissingHelper()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class Generator
            {
                public void Initialize()
                {
                    var provider = new SyntaxValueProvider();
                    provider.CreateSyntaxProvider(
                        (node, ct) => true,
                        (ctx, ct) => Parse());
                }

                private object Parse()
                {
                    return ExternalApi.Analyze();
                }
            }
            """;

        var expected = new DiagnosticResult("LSG004", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(13, 20, 13, 25)
            .WithArguments("Parse");

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task HelperWithCancellationTokenMustForwardToExternalCall_ReportsLSG005()
    {
        var code = """
            using System.Threading;
            using Microsoft.CodeAnalysis;

            public class Generator
            {
                public void Initialize()
                {
                    var provider = new SyntaxValueProvider();
                    provider.CreateSyntaxProvider(
                        (node, ct) => true,
                        (ctx, ct) => Parse(ct));
                }

                private object Parse(CancellationToken ct)
                {
                    return ExternalApi.Analyze();
                }
            }
            """;

        var expected = new DiagnosticResult("LSG005", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(16, 16, 16, 37);

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task HelperWithCancellationTokenMustCheckLoops_ReportsLSG005()
    {
        var code = """
            using System.Collections.Generic;
            using System.Threading;
            using Microsoft.CodeAnalysis;

            public class Generator
            {
                public void Initialize()
                {
                    var provider = new SyntaxValueProvider();
                    provider.CreateSyntaxProvider(
                        (node, ct) => true,
                        (ctx, ct) => Parse(ct));
                }

                private object Parse(CancellationToken ct)
                {
                    foreach (var item in new List<int>())
                    {
                    }

                    return null!;
                }
            }
            """;

        var expected = new DiagnosticResult("LSG005", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(17, 9, 19, 10);

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task HelperWithCancellationTokenAndLoopCheck_NoDiagnostic()
    {
        var code = """
            using System.Collections.Generic;
            using System.Threading;
            using Microsoft.CodeAnalysis;

            public class Generator
            {
                public void Initialize()
                {
                    var provider = new SyntaxValueProvider();
                    provider.CreateSyntaxProvider(
                        (node, ct) => true,
                        (ctx, ct) => Parse(ct));
                }

                private object Parse(CancellationToken ct)
                {
                    foreach (var item in new List<int>())
                    {
                        ct.ThrowIfCancellationRequested();
                    }

                    return ExternalApi.Analyze(ct);
                }
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task NonSourceGeneratorCancellationTokenMethod_DoesNotTriggerCallTreeRule()
    {
        var code = """
            using System.Collections.Generic;
            using System.Threading;

            public class Generator
            {
                public void Process(CancellationToken ct)
                {
                    Parse();
                }

                private object Parse()
                {
                    foreach (var item in new List<int>())
                    {
                    }

                    return null!;
                }
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code);
        await test.RunAsync();
    }
}
