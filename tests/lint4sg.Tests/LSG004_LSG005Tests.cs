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
            .WithSpan(7, 9, 7, 17);

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
}
