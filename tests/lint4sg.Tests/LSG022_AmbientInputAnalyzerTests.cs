using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG022_AmbientInputAnalyzerTests
{
    [Fact]
    public async Task FileReadInGenerator_ReportsLSG022()
    {
        var code = "using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { public void Initialize(object c) { var x = System.IO.File.ReadAllText(\"x\"); } }";
        await TestHelpers.CreateTest<AmbientInputAnalyzer>(code,
            new DiagnosticResult("LSG022", DiagnosticSeverity.Error).WithSpan(1, 116, 1, 147)
                .WithArguments("ReadAllText")).RunAsync();
    }

    [Fact]
    public async Task PurePathCombineInGenerator_DoesNotReport()
    {
        var code = "using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { public void Initialize(object c) { var x = System.IO.Path.Combine(\"a\", \"b\"); } }";
        await TestHelpers.CreateTest<AmbientInputAnalyzer>(code).RunAsync();
    }

    [Fact]
    public async Task AmbientReadInOrdinaryClass_DoesNotReport()
    {
        var code = "class Helper { string Read() => System.IO.File.ReadAllText(\"x\"); }";
        await TestHelpers.CreateTest<AmbientInputAnalyzer>(code).RunAsync();
    }

    [Fact]
    public async Task UncalledProjectHelperInGeneratorAssembly_DoesNotReport()
    {
        var code = "using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { public void Initialize(object c) {} } class Helper { string Read() => System.IO.File.ReadAllText(\"x\"); }";
        await TestHelpers.CreateTest<AmbientInputAnalyzer>(code).RunAsync();
    }

    [Fact]
    public async Task CalledProjectHelperInGeneratorAssembly_ReportsLSG022()
    {
        var code = "using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { public void Initialize(object c) { Helper.Read(); } } static class Helper { public static string Read() => System.IO.File.ReadAllText(\"x\"); }";
        await TestHelpers.CreateTest<AmbientInputAnalyzer>(code,
            new DiagnosticResult("LSG022", DiagnosticSeverity.Error).WithSpan(1, 180, 1, 211)
                .WithArguments("ReadAllText")).RunAsync();
    }

    [Fact]
    public async Task RegisteredCallbackCallingProjectHelper_ReportsLSG022()
    {
        var code = "using System; using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { public void Initialize(object c) { Callback.Register(() => Helper.Read()); } } static class Callback { public static void Register(Action callback) {} } static class Helper { public static string Read() => System.IO.File.ReadAllText(\"x\"); }";
        var start = code.IndexOf("System.IO.File.ReadAllText", System.StringComparison.Ordinal) + 1;
        await TestHelpers.CreateTest<AmbientInputAnalyzer>(code,
            new DiagnosticResult("LSG022", DiagnosticSeverity.Error)
                .WithSpan(1, start, 1, start + "System.IO.File.ReadAllText(\"x\")".Length)
                .WithArguments("ReadAllText")).RunAsync();
    }

    [Fact]
    public async Task RegisteredMethodGroupCallingProjectHelper_ReportsLSG022()
    {
        var code = "using System; using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { public void Initialize(object c) { Callback.Register(Helper.Read); } } static class Callback { public static void Register(Action callback) {} } static class Helper { public static void Read() { System.IO.File.ReadAllText(\"x\"); } }";
        var fragment = "System.IO.File.ReadAllText(\"x\")";
        var start = code.IndexOf(fragment, System.StringComparison.Ordinal) + 1;
        await TestHelpers.CreateTest<AmbientInputAnalyzer>(code,
            new DiagnosticResult("LSG022", DiagnosticSeverity.Error)
                .WithSpan(1, start, 1, start + fragment.Length)
                .WithArguments("ReadAllText")).RunAsync();
    }

    [Fact]
    public async Task UserDefinedFileType_DoesNotReport()
    {
        var code = "namespace System.IO { public static class File { public static string ReadAllText(string path) => path; } } namespace Demo { class Generator : Microsoft.CodeAnalysis.IIncrementalGenerator { public void Initialize(object c) { System.IO.File.ReadAllText(\"x\"); } } }";
        await TestHelpers.CreateTest<AmbientInputAnalyzer>(code).RunAsync();
    }

    [Theory]
    [InlineData("System.Environment.GetEnvironmentVariable(\"X\")", "GetEnvironmentVariable")]
    [InlineData("System.DateTime.Now", "Now")]
    [InlineData("System.Guid.NewGuid()", "NewGuid")]
    [InlineData("new System.Random()", "Random")]
    public async Task AmbientFrameworkInputs_ReportLSG022(string expression, string api)
    {
        var code = $"using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator {{ public void Initialize(object c) {{ var value = {expression}; }} }}";
        var start = code.IndexOf(expression, System.StringComparison.Ordinal) + 1;
        await TestHelpers.CreateTest<AmbientInputAnalyzer>(code,
            new DiagnosticResult("LSG022", DiagnosticSeverity.Error)
                .WithSpan(1, start, 1, start + expression.Length)
                .WithArguments(api)).RunAsync();
    }
}
