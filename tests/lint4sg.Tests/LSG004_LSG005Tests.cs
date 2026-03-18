using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using lint4sg.Analyzers;

namespace lint4sg.Tests;

public class LSG004_LSG005_CancellationTokenTests
{
    [Fact]
    public async Task MethodWithCTNotForwardedToSubMethod_ReportsLSG004()
    {
        var code = """
            using System.Threading;

            public class Generator
            {
                public void Process(CancellationToken cancellationToken)
                {
                    DoWork();
                }

                private void DoWork(CancellationToken ct = default) { }
            }
            """;

        var expected = new DiagnosticResult("LSG004", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(7, 9, 7, 17)
            .WithArguments("DoWork");

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task MethodWithCTForwardedToSubMethod_NoLSG004()
    {
        var code = """
            using System.Threading;

            public class Generator
            {
                public void Process(CancellationToken cancellationToken)
                {
                    DoWork(cancellationToken);
                }

                private void DoWork(CancellationToken ct = default) { }
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task RecursiveOwnCallChainWithoutCancellationToken_ReportsEveryMethodInChain()
    {
        var code = """
            using System.Threading;

            public class Generator
            {
                public void Transform(CancellationToken cancellationToken)
                {
                    A();
                }

                private void A()
                {
                    var a = 0;
                    a++;
                    a++;
                    B();
                }

                private void B()
                {
                    var b = 0;
                    b++;
                    b++;
                    C();
                }

                private void C()
                {
                    var c = 0;
                    c++;
                    c++;
                    c++;
                }
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code,
            new DiagnosticResult("LSG004", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(10, 18, 10, 19)
                .WithArguments("A"),
            new DiagnosticResult("LSG004", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(18, 18, 18, 19)
                .WithArguments("B"),
            new DiagnosticResult("LSG004", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .WithSpan(26, 18, 26, 19)
                .WithArguments("C"));

        await test.RunAsync();
    }

    [Fact]
    public async Task ShortLeafHelperWithoutCancellationToken_IsAllowed()
    {
        var code = """
            using System.Threading;

            public class Generator
            {
                public void Transform(CancellationToken cancellationToken)
                {
                    Log();
                }

                private void Log() => System.Console.WriteLine("trace");
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task MethodWithForLoopAndNoThrowIfCancelled_ReportsLSG005()
    {
        var code = """
            using System.Threading;

            public class Generator
            {
                public void Process(CancellationToken cancellationToken)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        DoWork();
                    }
                }

                private void DoWork() { }
            }
            """;

        var expected = new DiagnosticResult("LSG005", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(7, 9, 10, 10);

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task MethodWithForLoopAndThrowIfCancelled_NoLSG005()
    {
        var code = """
            using System.Threading;

            public class Generator
            {
                public void Process(CancellationToken cancellationToken)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        DoWork();
                    }
                }

                private void DoWork() { }
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task MethodWithForeachLoopAndNoThrowIfCancelled_ReportsLSG005()
    {
        var code = """
            using System.Threading;
            using System.Collections.Generic;

            public class Generator
            {
                public void Process(IEnumerable<string> items, CancellationToken cancellationToken)
                {
                    foreach (var item in items)
                    {
                        DoWork(item);
                    }
                }

                private void DoWork(string item) { }
            }
            """;

        var expected = new DiagnosticResult("LSG005", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(8, 9, 11, 10);

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task MethodWithNoLoop_NoLSG005()
    {
        var code = """
            using System.Threading;

            public class Generator
            {
                public void Process(CancellationToken cancellationToken)
                {
                    DoWork();
                    DoOtherWork();
                }

                private void DoWork() { }
                private void DoOtherWork() { }
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code);
        await test.RunAsync();
    }

    // ── Inferred lambda CancellationToken parameters ──────────────────────
    // Common in incremental-generator callbacks: `(ctx, ct) => ...` where the
    // parameter types are inferred from the Func<> delegate, not written explicitly.

    [Fact]
    public async Task Lambda_InferredCT_ForLoopWithoutThrowIfCancelled_ReportsLSG005()
    {
        // The lambda (ctx, ct) => { ... } has its CancellationToken 'ct' inferred
        // from SyntaxValueProvider.CreateSyntaxProvider's Func<..., CancellationToken, ...>.
        // LSG005 must still fire because the for-loop inside doesn't call
        // ThrowIfCancellationRequested().
        var code = """
            using System.Threading;
            using Microsoft.CodeAnalysis;

            public class Generator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    provider.CreateSyntaxProvider(
                        (node, ct) => true,
                        (ctx, ct) =>
                        {
                            for (int i = 0; i < 10; i++)
                            {
                            }
                            return null!;
                        });
                }
            }
            """;

        var expected = new DiagnosticResult("LSG005", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(13, 17, 15, 18);

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task Lambda_InferredCT_ForwardedToSubMethod_NoLSG004()
    {
        // With a properly forwarded inferred CancellationToken, LSG004 must NOT fire.
        var code = """
            using System.Threading;
            using Microsoft.CodeAnalysis;

            public class Generator
            {
                public void Initialize(object context)
                {
                    var provider = new SyntaxValueProvider();
                    provider.CreateSyntaxProvider(
                        (node, ct) => true,
                        (ctx, ct) => DoWork(ct));
                }

                private object DoWork(CancellationToken token = default) => null!;
            }
            """;

        var test = TestHelpers.CreateTest<CancellationTokenAnalyzer>(code);
        await test.RunAsync();
    }
}
