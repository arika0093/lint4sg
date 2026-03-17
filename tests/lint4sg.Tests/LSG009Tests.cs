using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using lint4sg.Analyzers;

namespace lint4sg.Tests;

public class LSG009_NormalizeWhitespaceTests
{
    [Fact]
    public async Task NormalizeWhitespace_ReportsLSG009()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class CodeBuilder
            {
                public SyntaxNode Build(SyntaxNode node)
                {
                    return node.NormalizeWhitespace();
                }
            }
            """;

        var expected = new DiagnosticResult("LSG009", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(7, 16, 7, 42);

        var test = TestHelpers.CreateTest<NormalizeWhitespaceAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task UserDefinedNormalizeWhitespace_NoError()
    {
        var code = """
            public class Helper
            {
                public object NormalizeWhitespace() => null!;
            }

            public class User
            {
                public void Use()
                {
                    var h = new Helper();
                    var r = h.NormalizeWhitespace();
                }
            }
            """;

        // User-defined NormalizeWhitespace should NOT trigger LSG009
        // (only Roslyn's SyntaxNode.NormalizeWhitespace should be flagged)
        var test = TestHelpers.CreateTest<NormalizeWhitespaceAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task NoNormalizeWhitespace_NoError()
    {
        var code = """
            public class CodeBuilder
            {
                public string Build()
                {
                    return "    hello world";
                }
            }
            """;

        var test = TestHelpers.CreateTest<NormalizeWhitespaceAnalyzer>(code);
        await test.RunAsync();
    }
}
