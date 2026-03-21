using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG005_CancellationTokenAnalyzerTests
{
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

        var expected = new DiagnosticResult(
            "LSG005",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(16, 16, 16, 37);

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

        var expected = new DiagnosticResult(
            "LSG005",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(17, 9, 19, 10);

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
    public async Task HelperWithCancellationTokenForwardedToExternalCall_NoDiagnostic()
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
                    return ExternalApi.Analyze(ct);
                }
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task HelperWithCancellationTokenCallingProjectOverloadWithoutCt_ReportsLSG005()
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
                    return Analyze();
                }

                private object Analyze() => null!;
                private object Analyze(CancellationToken ct) => null!;
            }
            """;

        var expected = new DiagnosticResult(
            "LSG005",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(16, 16, 16, 25);

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task HelperWithCancellationTokenReferencedInWrongArgument_ReportsLSG005()
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
                    return Analyze(ct.ToString(), default);
                }

                private object Analyze(string text, CancellationToken cancellationToken)
                {
                    return ExternalApi.Analyze(cancellationToken);
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG005",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(16, 16, 16, 47);

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task LoopWithThrowIfCancelledOnlyInsideNestedLambda_ReportsLSG005()
    {
        var code = """
            using System;
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
                        Action action = () => ct.ThrowIfCancellationRequested();
                    }

                    return null!;
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG005",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(18, 9, 21, 10);

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }
}
