using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG012_ProjectFileAnalyzerTests
{
    [Fact]
    public async Task ExternalPackage_ReportsLSG012()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(
            csproj,
            new DiagnosticResult("LSG012", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 68)
                .WithArguments("Newtonsoft.Json")
        );
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

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(csproj);
    }

    [Fact]
    public async Task ExternalPackageWithPrivateAssetsAll_NoLSG012()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Polyfill" Version="9.22.0" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """;

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(csproj);
    }

    [Fact]
    public async Task ExternalPackageWithPrivateAssetsElementAll_NoLSG012()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Polyfill">
                  <Version>9.22.0</Version>
                  <PrivateAssets>all</PrivateAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(csproj);
    }

    [Fact]
    public async Task MultipleExternalPackages_ReportsLSG012ForEach()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Serilog" Version="3.1.0" />
                <PackageReference Include="AutoMapper" Version="12.0.0" />
              </ItemGroup>
            </Project>
            """;

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(
            csproj,
            new DiagnosticResult("LSG012", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 59)
                .WithArguments("Serilog"),
            new DiagnosticResult("LSG012", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 4, 1, 4, 63)
                .WithArguments("AutoMapper")
        );
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

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(csproj);
    }

    [Fact]
    public async Task MultilinePackageReference_ReportsLSG012()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json">
                  <Version>13.0.3</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(
            csproj,
            new DiagnosticResult("LSG012", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 49)
                .WithArguments("Newtonsoft.Json")
        );
    }
}
