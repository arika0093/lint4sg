using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using lint4sg.Analyzers;

namespace lint4sg.Tests;

public class LSG102_LSG103_PerformanceTests
{
    [Fact]
    public async Task StringFormatCall_ReportsLSG102()
    {
        var code = """
            public class Generator
            {
                public string Build(string name, int count)
                {
                    return string.Format("Hello {0}, count: {1}", name, count);
                }
            }
            """;

        var expected = new DiagnosticResult("LSG102", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithSpan(5, 16, 5, 67);

        var test = TestHelpers.CreateTest<PerformanceAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task InterpolatedString_NoLSG102()
    {
        var code = """
            public class Generator
            {
                public string Build(string name, int count)
                {
                    return $"Hello {name}, count: {count}";
                }
            }
            """;

        var test = TestHelpers.CreateTest<PerformanceAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task StringConcatenationInLoop_ReportsLSG103()
    {
        var code = """
            using System.Collections.Generic;

            public class Generator
            {
                public string Build(IEnumerable<string> items)
                {
                    string result = "";
                    foreach (var item in items)
                    {
                        result = result + item;
                    }
                    return result;
                }
            }
            """;

        var expected = new DiagnosticResult("LSG103", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithSpan(10, 22, 10, 35);

        var test = TestHelpers.CreateTest<PerformanceAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task StringConcatenationOutsideLoop_NoLSG103()
    {
        var code = """
            public class Generator
            {
                public string Build(string a, string b)
                {
                    return a + b;
                }
            }
            """;

        var test = TestHelpers.CreateTest<PerformanceAnalyzer>(code);
        await test.RunAsync();
    }
}
