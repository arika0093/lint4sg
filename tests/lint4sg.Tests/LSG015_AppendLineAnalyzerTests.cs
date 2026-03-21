using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG015_AppendLineAnalyzerTests
{
    [Fact]
    public async Task RawStringLiteralWithSharedIndentation_ReportsLSG015ButNotLSG010()
    {
        var code = """"
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.Append("""
                            public class Foo
                            {
                            }
                        """);
                }
            }
            """";

        var expected = new DiagnosticResult(
            "LSG015",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(7, 9, 11, 17);

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task RawStringLiteralWithoutSharedIndentation_NoWhitespaceDiagnostic()
    {
        var code = """"
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.Append("""
                        public class Foo
                    {
                    }
                    """);
                }
            }
            """";

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task SingleLineRawStringLiteral_NoLSG015()
    {
        var code = """"
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.Append("""    public class Foo""");
                }
            }
            """";

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }
}
