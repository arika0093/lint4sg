using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
#pragma warning disable CS0618
using Microsoft.CodeAnalysis.Testing.Verifiers;
#pragma warning restore CS0618

namespace lint4sg.Tests;

internal static class ProjectFileAnalyzerTestHelpers
{
    public static Task RunProjectFileTestAsync(
        string csprojContent,
        params DiagnosticResult[] expected
    )
    {
        var test = new CSharpAnalyzerTest<ProjectFileAnalyzer, XUnitVerifier>
        {
            TestState =
            {
                Sources = { "// placeholder" },
                AdditionalFiles = { ("test.csproj", csprojContent) },
            },
        };
        test.TestState.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }
}
