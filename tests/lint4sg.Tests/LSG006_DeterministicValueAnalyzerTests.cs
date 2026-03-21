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
}
