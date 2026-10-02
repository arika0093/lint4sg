using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG006_DeterministicValueAnalyzerTests
{
    [Fact]
    public async Task ISymbol_DirectToRegisterSourceOutput_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<ISymbol> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, sym) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(9, 9, 9, 62)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task SemanticModel_DirectToRegisterSourceOutput_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<SemanticModel> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, sm) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(9, 9, 9, 61)
                .WithArguments("Microsoft.CodeAnalysis.SemanticModel")
        );
    }

    [Fact]
    public async Task Compilation_DirectToRegisterSourceOutput_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<Compilation> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, comp) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(9, 9, 9, 63)
                .WithArguments("Microsoft.CodeAnalysis.Compilation")
        );
    }

    [Fact]
    public async Task UserDefinedMutableClass_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public class MyData
            {
                public string Name { get; set; } = "";
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<MyData> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, d) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(14, 9, 14, 60)
                .WithArguments("MyData")
        );
    }

    [Fact]
    public async Task ReferenceTypeWithValueEqualityAndDeterministicMembers_NoLSG006()
    {
        var code = """
            using System;
            using Microsoft.CodeAnalysis;

            public sealed class StableData : IEquatable<StableData>
            {
                public string Name { get; }
                public int Count { get; }

                public StableData(string name, int count)
                {
                    Name = name;
                    Count = count;
                }

                public bool Equals(StableData? other) => other is not null && Name == other.Name && Count == other.Count;
                public override bool Equals(object? obj) => obj is StableData other && Equals(other);
                public override int GetHashCode() => HashCode.Combine(Name, Count);
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<StableData> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, data) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task ReferenceTypeWithValueEqualityButISymbolMember_ReportsLSG006()
    {
        var code = """
            using System;
            using Microsoft.CodeAnalysis;

            public sealed class StableData : IEquatable<StableData>
            {
                public ISymbol Symbol { get; }

                public StableData(ISymbol symbol)
                {
                    Symbol = symbol;
                }

                public bool Equals(StableData? other) => ReferenceEquals(Symbol, other?.Symbol);
                public override bool Equals(object? obj) => obj is StableData other && Equals(other);
                public override int GetHashCode() => 0;
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<StableData> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, data) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(24, 9, 24, 63)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task ReferenceTypeWithValueEqualityButPrivateISymbolField_ReportsLSG006()
    {
        var code = """
            using System;
            using Microsoft.CodeAnalysis;

            public sealed class StableData : IEquatable<StableData>
            {
                private readonly ISymbol _symbol;

                public StableData(ISymbol symbol)
                {
                    _symbol = symbol;
                }

                public bool Equals(StableData? other) => ReferenceEquals(_symbol, other?._symbol);
                public override bool Equals(object? obj) => obj is StableData other && Equals(other);
                public override int GetHashCode() => 0;
            }

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<StableData> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, data) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(24, 9, 24, 63)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task RecordWithISymbolProperty_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public record MyInfo(string Name, ISymbol Symbol);

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<MyInfo> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, info) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(11, 9, 11, 63)
                .WithArguments("Microsoft.CodeAnalysis.ISymbol")
        );
    }

    [Fact]
    public async Task RecordWithCompilationNestedTwoLevels_ReportsLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public record Inner(string Text, Compilation Comp);
            public record Outer(int Id, Inner Data);

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<Outer> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, o) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(
            code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(12, 9, 12, 60)
                .WithArguments("Microsoft.CodeAnalysis.Compilation")
        );
    }

    [Fact]
    public async Task RecordWithOnlyEquatableMembers_NoLSG006()
    {
        var code = """
            using Microsoft.CodeAnalysis;

            public record MyInfo(string Name, int Count);

            public class MyGenerator
            {
                public void Run(
                    IncrementalGeneratorInitializationContext ctx,
                    IncrementalValueProvider<MyInfo> provider)
                {
                    ctx.RegisterSourceOutput(provider, (spc, info) => { });
                }
            }
            """;

        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task UnsafeValueInIntermediateSelect_ReportsBeforeFinalProjection()
    {
        var code = "using Microsoft.CodeAnalysis; class G { void Run(IncrementalValuesProvider<string> input) { var symbols = input.Select(static (s, ct) => (ISymbol)null!); var names = symbols.Select(static (s, ct) => s.ToString()); } }";
        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code,
            new DiagnosticResult("LSG008", DiagnosticSeverity.Warning)
                .WithSpan(1, 107, 1, 153).WithArguments("Microsoft.CodeAnalysis.ISymbol"));
    }

    [Fact]
    public async Task BroadCompilationProviderCombinedBeforeProjection_ReportsLSG008()
    {
        var code = "using Microsoft.CodeAnalysis; class G { void Run(IncrementalValueProvider<int> items, IncrementalValueProvider<Compilation> compilation) { var late = items.Combine(compilation).Select(static (pair, ct) => pair.Right != null); } }";
        var start = code.IndexOf("items.Combine(compilation)", System.StringComparison.Ordinal) + 1;
        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code,
            new DiagnosticResult("LSG008", DiagnosticSeverity.Warning)
                .WithSpan(1, start, 1, start + "items.Combine(compilation)".Length)
                .WithArguments("Microsoft.CodeAnalysis.Compilation"));
    }

    [Fact]
    public async Task ProjectBroadCompilationProviderBeforeCombine_IsAccepted()
    {
        var code = "using Microsoft.CodeAnalysis; class G { void Run(IncrementalValueProvider<int> items, IncrementalValueProvider<Compilation> compilation) { var feature = compilation.Select(static (c, ct) => c != null); var result = items.Combine(feature); } }";
        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task UnsafeValueInIntermediateSelectMany_ReportsLSG008()
    {
        var code = "using Microsoft.CodeAnalysis; class Data { public string Name { get; set; } = \"\"; } class G { void Run(IncrementalValuesProvider<string> input) { var data = IncrementalProviderExtensions.SelectMany(input, static (s, ct) => (System.Collections.Generic.IEnumerable<Data>)null!); } }";
        var fragment = "IncrementalProviderExtensions.SelectMany(input, static (s, ct) => (System.Collections.Generic.IEnumerable<Data>)null!)";
        var start = code.IndexOf(fragment, System.StringComparison.Ordinal) + 1;
        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code,
            new DiagnosticResult("LSG008", DiagnosticSeverity.Warning)
                .WithSpan(1, start, 1, start + fragment.Length)
                .WithArguments("Data"));
    }

    [Fact]
    public async Task CollectOfRoslynState_ReportsLSG008()
    {
        var code = "using Microsoft.CodeAnalysis; class G { void Run(IncrementalValuesProvider<string> input) { var symbols = IncrementalProviderExtensions.Collect(input); } }";
        var fragment = "IncrementalProviderExtensions.Collect(input)";
        var start = code.IndexOf(fragment, System.StringComparison.Ordinal) + 1;
        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code,
            new DiagnosticResult("LSG008", DiagnosticSeverity.Warning)
                .WithSpan(1, start, 1, start + fragment.Length)
                .WithArguments("string[]"));
    }

    [Fact]
    public async Task UserDefinedCompilationType_IsNotTreatedAsRoslynState()
    {
        var code = "using Microsoft.CodeAnalysis; public class Compilation { public string Name { get; } = \"x\"; } class G { void Run(IncrementalValueProvider<Compilation> input) { } }";
        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task UserDefinedRoslynNamespaceCompilation_IsNotTreatedAsCompilerState()
    {
        var code = "namespace Microsoft.CodeAnalysis { public sealed class Compilation : System.IEquatable<Compilation> { public string Name { get; } = \"x\"; public bool Equals(Compilation? other) => other?.Name == Name; } } class G { void Run(Microsoft.CodeAnalysis.IncrementalGeneratorInitializationContext c, Microsoft.CodeAnalysis.IncrementalValueProvider<Microsoft.CodeAnalysis.Compilation> value) { c.RegisterSourceOutput(value, static (_, _) => {}); } }";
        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code);
    }

    [Fact]
    public async Task MutableCustomEquatableClass_IsRejected()
    {
        var code = "using System; using Microsoft.CodeAnalysis; public sealed class Mutable : IEquatable<Mutable> { public string Name { get; set; } = \"\"; public bool Equals(Mutable? other) => other?.Name == Name; public override bool Equals(object? other) => other is Mutable value && Equals(value); public override int GetHashCode() => Name.GetHashCode(); } class G { void Run(IncrementalGeneratorInitializationContext c, IncrementalValueProvider<Mutable> value) { c.RegisterSourceOutput(value, static (_, _) => {}); } }";
        await DeterministicValueAnalyzerTestHelpers.RunTestAsync(code,
            new DiagnosticResult("LSG006", DiagnosticSeverity.Error)
                .WithSpan(1, 448, 1, 498).WithArguments("Mutable"));
    }
}
