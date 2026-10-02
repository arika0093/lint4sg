using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG013_ReflectionApiAnalyzerTests
{
    [Fact]
    public async Task UsingSystemReflectionWithoutUsingAnApi_DoesNotReport()
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

        var test = TestHelpers.CreateTest<ReflectionApiAnalyzer>(code);
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

    [Fact]
    public async Task TypeReflectionCallWithAlias_ReportsLSG013()
    {
        var code = "using T = System.Type; class Generator { object Run() { T type = typeof(string); return type.GetProperties(); } }";
        var start = code.IndexOf("type.GetProperties()", System.StringComparison.Ordinal) + 1;
        var test = TestHelpers.CreateTest<ReflectionApiAnalyzer>(code,
            new DiagnosticResult("LSG013", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(1, start, 1, start + "type.GetProperties()".Length).WithArguments("GetProperties"));
        await test.RunAsync();
    }

    [Fact]
    public async Task UnrelatedMethodWithReflectionLikeName_DoesNotReport()
    {
        var code = "class Helper { public void GetProperties() {} } class Generator { void Run() => new Helper().GetProperties(); }";
        var test = TestHelpers.CreateTest<ReflectionApiAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task UserDefinedTypeInReflectionNamespace_DoesNotReport()
    {
        var code = "namespace System.Reflection { public class MethodInfo { public void Invoke() {} } } class G { void M() { new System.Reflection.MethodInfo().Invoke(); } }";
        await TestHelpers.CreateTest<ReflectionApiAnalyzer>(code).RunAsync();
    }

    [Fact]
    public async Task TypeAssemblyProperty_ReportsLSG013()
    {
        var code = "class G { object M() => typeof(string).Assembly; }";
        await TestHelpers.CreateTest<ReflectionApiAnalyzer>(code,
            new DiagnosticResult("LSG013", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(1, 25, 1, 48).WithArguments("Assembly")).RunAsync();
    }

    [Fact]
    public async Task ReflectionWrappedByProjectHelper_ReportsLSG013()
    {
        var code = "using System; static class Helper { public static object GetProperties(Type type) => type.GetProperties(); } class G { object Run() => Helper.GetProperties(typeof(string)); }";
        var fragment = "type.GetProperties()";
        var start = code.IndexOf(fragment, System.StringComparison.Ordinal) + 1;
        await TestHelpers.CreateTest<ReflectionApiAnalyzer>(code,
            new DiagnosticResult("LSG013", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(1, start, 1, start + fragment.Length).WithArguments("GetProperties")).RunAsync();
    }
}
