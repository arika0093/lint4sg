using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG013_ReflectionApiTests
{
    [Fact]
    public async Task UsingSystemReflection_ReportsLSG013()
    {
        var code = """
            using System.Reflection;

            public class Generator
            {
                public void Generate()
                {
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG013",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
        )
            .WithSpan(1, 1, 1, 25)
            .WithArguments("System.Reflection");

        var test = TestHelpers.CreateTest<ReflectionApiAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task StringWithReflectionNamespace_ReportsLSG013()
    {
        var code = """
            public class Generator
            {
                public string GetCode()
                {
                    return "using System.Reflection;";
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG013",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
        )
            .WithSpan(5, 16, 5, 42)
            .WithArguments("System.Reflection");

        var test = TestHelpers.CreateTest<ReflectionApiAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task StringWithGetTypeCall_ReportsLSG013()
    {
        var code = """
            public class Generator
            {
                public string GetCode()
                {
                    return "var type = obj.GetType()";
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG013",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
        ).WithSpan(5, 16, 5, 42);

        var test = TestHelpers.CreateTest<ReflectionApiAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task NoReflection_NoWarning()
    {
        var code = """
            using System;

            public class Generator
            {
                public string GetCode()
                {
                    return "public class Foo { }";
                }
            }
            """;

        var test = TestHelpers.CreateTest<ReflectionApiAnalyzer>(code);
        await test.RunAsync();
    }
}
