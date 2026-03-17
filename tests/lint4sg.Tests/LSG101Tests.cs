using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
#pragma warning disable CS0618
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;
using lint4sg.Analyzers;

namespace lint4sg.Tests;

public class LSG101_PerformanceStructTests
{
    [Fact]
    public async Task LargeStructParameter_ReportsLSG101()
    {
        var code = """
            public struct LargeStruct
            {
                public int A;
                public int B;
                public int C;
            }

            public class Processor
            {
                public void Process(LargeStruct data)
                {
                }
            }
            """;

        var expected = new DiagnosticResult("LSG101", DiagnosticSeverity.Info)
            .WithSpan(10, 25, 10, 41)
            .WithArguments("data");

        var test = TestHelpers.CreateTest<PerformanceAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task SmallStructParameter_NoLSG101()
    {
        var code = """
            public struct SmallStruct
            {
                public int Value;
            }

            public class Processor
            {
                public void Process(SmallStruct data)
                {
                }
            }
            """;

        // Small struct (1 field) should not trigger LSG101
        var test = TestHelpers.CreateTest<PerformanceAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task LargeStructWithInModifier_NoLSG101()
    {
        var code = """
            public struct LargeStruct
            {
                public int A;
                public int B;
                public int C;
            }

            public class Processor
            {
                public void Process(in LargeStruct data)
                {
                }
            }
            """;

        // Already has 'in' modifier - no diagnostic
        var test = TestHelpers.CreateTest<PerformanceAnalyzer>(code);
        await test.RunAsync();
    }
}
