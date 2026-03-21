using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG011_AppendLineAnalyzerTests
{
    [Fact]
    public async Task ThreeConsecutiveAppendLines_ReportsLSG011()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("namespace Foo");
                    sb.AppendLine("{");
                    sb.AppendLine("    public class Bar");
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG011",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        )
            .WithSpan(7, 9, 7, 40)
            .WithArguments("3");

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task TwoConsecutiveAppendLines_NoLSG011()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("namespace Foo");
                    sb.AppendLine("{");
                }
            }
            """;

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task ThreeAppendLinesWithBranchingBetween_NoLSG011()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb, bool includeBase)
                {
                    sb.AppendLine("namespace Foo");
                    if (includeBase) sb.AppendLine("// base");
                    sb.AppendLine("{");
                    sb.AppendLine("}");
                }
            }
            """;

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }
}
