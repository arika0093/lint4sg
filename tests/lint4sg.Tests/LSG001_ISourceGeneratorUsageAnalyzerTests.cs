using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG001_ISourceGeneratorUsageAnalyzerTests
{
    [Fact]
    public async Task ClassImplementingISourceGenerator_ReportsLSG001()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            [Generator]
            public class MyGenerator : ISourceGenerator
            {
                public void Initialize(object context) { }
                public void Execute(object context) { }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG001",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(4, 28, 4, 44);

        var test = TestHelpers.CreateTest<ISourceGeneratorUsageAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task ClassImplementingIIncrementalGenerator_NoLSG001()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            [Generator]
            public class MyGenerator : IIncrementalGenerator
            {
                public void Initialize(object context) { }
            }
            """;

        var test = TestHelpers.CreateTest<ISourceGeneratorUsageAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task ClassWithNonGeneratorBaseType_NoLSG001()
    {
        var code = """
            public interface ISomething { }

            public class MyClass : ISomething { }
            """;

        var test = TestHelpers.CreateTest<ISourceGeneratorUsageAnalyzer>(code);
        await test.RunAsync();
    }
}
