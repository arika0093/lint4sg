using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using lint4sg.Analyzers;

namespace lint4sg.Tests;

public class LSG010_LSG011_AppendLineTests
{
    [Fact]
    public async Task AppendLineWithEightSpaces_ReportsLSG010()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("        public class Foo");
                }
            }
            """;

        var expected = new DiagnosticResult("LSG010", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(7, 9, 7, 50);

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithTwoTabs_ReportsLSG010()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("\t\tpublic class Foo");
                }
            }
            """;

        var expected = new DiagnosticResult("LSG010", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(7, 9, 7, 46);

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithFourSpaces_NoError()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("    public class Foo");
                }
            }
            """;

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }

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

        var expected = new DiagnosticResult("LSG011", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
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
