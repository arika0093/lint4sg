using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG010_AppendLineAnalyzerTests
{
    [Fact]
    public async Task AppendLineWithEightSpaces_ReportsLSG010()
    {
        var code = """"
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("        public class Foo");
                }
            }
            """";

        var expected = new DiagnosticResult(
            "LSG010",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(7, 9, 7, 50);

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithTwoTabs_ReportsLSG010()
    {
        var code = """"
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("\t\tpublic class Foo");
                }
            }
            """";

        var expected = new DiagnosticResult(
            "LSG010",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(7, 9, 7, 46);

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithFourSpaces_NoError()
    {
        var code = """"
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("    public class Foo");
                }
            }
            """";

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }
}
