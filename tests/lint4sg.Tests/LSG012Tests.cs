using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
#pragma warning disable CS0618
using Microsoft.CodeAnalysis.Testing.Verifiers;
#pragma warning restore CS0618
using Xunit;
using lint4sg.Analyzers;

namespace lint4sg.Tests;

/// <summary>
/// Tests for LSG012 (external dependency) and LSG014 (too-new CodeAnalysis version)
/// via AdditionalFiles (simulated .csproj content).
/// </summary>
public class LSG012_ProjectFileTests
{
    private static Task RunProjectFileTestAsync(
        string csprojContent,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ProjectFileAnalyzer, XUnitVerifier>
        {
            TestState =
            {
                Sources = { "// placeholder" },
                AdditionalFiles = { ("test.csproj", csprojContent) }
            }
        };
        test.TestState.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    [Fact]
    public async Task ExternalPackage_ReportsLSG012()
    {
        // Line 3 = "    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />" (67 chars)
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;

        await RunProjectFileTestAsync(csproj,
            new DiagnosticResult("LSG012", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 68)
                .WithArguments("Newtonsoft.Json"));
    }

    [Fact]
    public async Task MicrosoftCodeAnalysisPackage_NoLSG012()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
              </ItemGroup>
            </Project>
            """;

        await RunProjectFileTestAsync(csproj);
    }

    [Fact]
    public async Task MultipleExternalPackages_ReportsLSG012ForEach()
    {
        // Line 3 = "    <PackageReference Include="Serilog" Version="3.1.0" />" (58 chars)
        // Line 4 = "    <PackageReference Include="AutoMapper" Version="12.0.0" />" (62 chars)
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Serilog" Version="3.1.0" />
                <PackageReference Include="AutoMapper" Version="12.0.0" />
              </ItemGroup>
            </Project>
            """;

        await RunProjectFileTestAsync(csproj,
            new DiagnosticResult("LSG012", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 59)
                .WithArguments("Serilog"),
            new DiagnosticResult("LSG012", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 4, 1, 4, 63)
                .WithArguments("AutoMapper"));
    }

    [Fact]
    public async Task NoPackageReferences_NoWarnings()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;

        await RunProjectFileTestAsync(csproj);
    }

    [Fact]
    public async Task CodeAnalysisCSharp_Version5_ReportsLSG014()
    {
        // Line 3 = "    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />" (80 chars)
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
              </ItemGroup>
            </Project>
            """;

        await RunProjectFileTestAsync(csproj,
            new DiagnosticResult("LSG014", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 81)
                .WithArguments("5.0.0"));
    }

    [Fact]
    public async Task CodeAnalysisCSharp_Version4_NoLSG014()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
              </ItemGroup>
            </Project>
            """;

        await RunProjectFileTestAsync(csproj);
    }

    [Fact]
    public async Task MultilinePackageReference_ReportsLSG012()
    {
        // Multi-line format where Include and Version are separated across elements.
        // The old line-split parser would have missed the Version child element;
        // the XML-based parser handles this correctly.
        // Line 3 = "    <PackageReference Include="Newtonsoft.Json">" (48 chars)
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json">
                  <Version>13.0.3</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        await RunProjectFileTestAsync(csproj,
            new DiagnosticResult("LSG012", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 49)
                .WithArguments("Newtonsoft.Json"));
    }

    [Fact]
    public async Task MultilineCodeAnalysisCSharp_Version5_ReportsLSG014()
    {
        // Same multi-line format but for the version-gating rule.
        // Line 3 = "    <PackageReference Include="Microsoft.CodeAnalysis.CSharp">" (62 chars)
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp">
                  <Version>5.0.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        await RunProjectFileTestAsync(csproj,
            new DiagnosticResult("LSG014", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 63)
                .WithArguments("5.0.0"));
    }
}
