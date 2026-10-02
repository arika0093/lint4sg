using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG023_StatelessGeneratorAnalyzerTests
{
    [Fact]
    public async Task MutableFieldOnGenerator_ReportsLSG023()
    {
        var code = "using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { private int _count; public void Initialize(object c) { _count++; } }";
        await TestHelpers.CreateTest<StatelessGeneratorAnalyzer>(code,
            new DiagnosticResult("LSG023", DiagnosticSeverity.Error).WithSpan(1, 85, 1, 91)
                .WithArguments("_count")).RunAsync();
    }

    [Fact]
    public async Task StaticReadonlyAndConstMembers_AreAllowed()
    {
        var code = "using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { private const int C = 1; private static readonly string Name = \"g\"; public void Initialize(object c) {} }";
        await TestHelpers.CreateTest<StatelessGeneratorAnalyzer>(code).RunAsync();
    }

    [Fact]
    public async Task GetterOnlyImmutableConfiguration_IsAllowed()
    {
        var code = "using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { private readonly string _name; public Generator(string name) { _name = name; } public void Initialize(object c) {} }";
        await TestHelpers.CreateTest<StatelessGeneratorAnalyzer>(code).RunAsync();
    }

    [Fact]
    public async Task ReadonlyMutableCollectionField_ReportsLSG023()
    {
        var code = "using System.Collections.Generic; using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator\n{\n    private readonly List<string> _items = new();\n    public void Initialize(object c) {}\n}";
        await TestHelpers.CreateTest<StatelessGeneratorAnalyzer>(code,
            new DiagnosticResult("LSG023", DiagnosticSeverity.Error).WithSpan(3, 35, 3, 41)
                .WithArguments("_items")).RunAsync();
    }

    [Fact]
    public async Task MutableStateInheritedFromOrdinaryBase_ReportsLSG023()
    {
        var code = "using Microsoft.CodeAnalysis; abstract class GeneratorBase { protected int _count; } class Generator : GeneratorBase, IIncrementalGenerator { public void Initialize(object c) {} }";
        await TestHelpers.CreateTest<StatelessGeneratorAnalyzer>(code,
            new DiagnosticResult("LSG023", DiagnosticSeverity.Error).WithSpan(1, 76, 1, 82)
                .WithArguments("_count")).RunAsync();
    }

    [Fact]
    public async Task GetterOnlyMutableCollectionProperty_ReportsLSG023()
    {
        var code = "using System.Collections.Generic; using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { private List<string> Items { get; } = new(); public void Initialize(object c) {} }";
        var start = code.IndexOf("Items", System.StringComparison.Ordinal) + 1;
        await TestHelpers.CreateTest<StatelessGeneratorAnalyzer>(code,
            new DiagnosticResult("LSG023", DiagnosticSeverity.Error)
                .WithSpan(1, start, 1, start + "Items".Length).WithArguments("Items")).RunAsync();
    }

    [Fact]
    public async Task MutableFieldWrittenByRegisteredCallback_ReportsLSG023()
    {
        var code = "using System; using Microsoft.CodeAnalysis; class Generator : IIncrementalGenerator { private int _count; public void Initialize(object c) { Callback.Register(() => _count++); } } static class Callback { public static void Register(Action callback) {} }";
        var start = code.IndexOf("_count", System.StringComparison.Ordinal) + 1;
        await TestHelpers.CreateTest<StatelessGeneratorAnalyzer>(code,
            new DiagnosticResult("LSG023", DiagnosticSeverity.Error)
                .WithSpan(1, start, 1, start + "_count".Length).WithArguments("_count")).RunAsync();
    }
}
