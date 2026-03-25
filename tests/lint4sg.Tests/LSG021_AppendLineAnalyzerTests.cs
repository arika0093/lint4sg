using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG021_AppendLineAnalyzerTests
{
    [Fact]
    public async Task AppendLineWithUnqualifiedObjectCreation_ReportsLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("var x = new MyClass();");
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG021",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(7, 9, 7, 48);

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithMethodSignatureTypes_ReportsLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("public Foo GetFoo(this Bar bar, int a, string b, Buz c)");
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG021",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(7, 9, 7, 81);

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithLocalVariableStringValue_ReportsLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    var line = "var x = new MyClass();";
                    sb.AppendLine(line);
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG021",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(8, 9, 8, 28);

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithFullyQualifiedGenericType_NoLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("global::System.Collections.Generic.List<global::MyNamespace.Bar> items;");
                }
            }
            """;

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithBuiltInTypesOnly_NoLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("string text = \"hello\";");
                }
            }
            """;

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithVarAndFullyQualifiedCreation_NoLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("var value = new global::MyNamespace.MyType();");
                }
            }
            """;

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithDynamic_NoLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("dynamic value = default;");
                }
            }
            """;

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithGenericTypeParameters_NoLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    sb.AppendLine("public TOutput Map<TInput, TOutput>(TInput value)");
                }
            }
            """;

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithInterpolatedTypeVariables_ReportsLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    var resultType = "Foo";
                    var argType = "Bar";
                    sb.AppendLine($"public {resultType} FunctionName({argType} arg)");
                }
            }
            """;

        var expected = new DiagnosticResult(
            "LSG021",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        ).WithSpan(9, 9, 9, 74);

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code, expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task AppendLineWithInterpolatedFullyQualifiedTypeVariables_NoLSG021()
    {
        var code = """
            using System.Text;

            public class Generator
            {
                public void Generate(StringBuilder sb)
                {
                    var resultType = "global::MyNamespace.Foo";
                    var argType = "global::MyNamespace.Bar";
                    sb.AppendLine($"public {resultType} FunctionName({argType} arg)");
                }
            }
            """;

        var test = TestHelpers.CreateTest<AppendLineAnalyzer>(code);
        await test.RunAsync();
    }
}
