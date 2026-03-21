using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace lint4sg.Tests;

public class LSG014_ProjectFileAnalyzerTests
{
    [Fact]
    public async Task CodeAnalysisCSharp_Version5_ReportsLSG014()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
              </ItemGroup>
            </Project>
            """;

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(
            csproj,
            new DiagnosticResult("LSG014", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 81)
                .WithArguments("5.0.0")
        );
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

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(csproj);
    }

    [Fact]
    public async Task MultilineCodeAnalysisCSharp_Version5_ReportsLSG014()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp">
                  <Version>5.0.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        await ProjectFileAnalyzerTestHelpers.RunProjectFileTestAsync(
            csproj,
            new DiagnosticResult("LSG014", DiagnosticSeverity.Warning)
                .WithSpan("test.csproj", 3, 1, 3, 63)
                .WithArguments("5.0.0")
        );
    }
}
