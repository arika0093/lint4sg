using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG012: External NuGet package reference in source generator project.
/// LSG014: Microsoft.CodeAnalysis.CSharp version >= 5.0.0.
/// These analyzers work on .csproj additional files.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProjectFileAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> AllowedPackagePrefixes = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Microsoft.CodeAnalysis",
        "Microsoft.Net.Compilers");

    private const string CodeAnalysisCSharpPackage = "Microsoft.CodeAnalysis.CSharp";
    private static readonly Version MaxRecommendedVersion = new(5, 0, 0);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.LSG012, DiagnosticDescriptors.LSG014);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterAdditionalFileAction(AnalyzeAdditionalFile);
    }

    private static void AnalyzeAdditionalFile(AdditionalFileAnalysisContext context)
    {
        var file = context.AdditionalFile;
        var filePath = file.Path;

        // Only process .csproj files
        if (!filePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return;

        var content = file.GetText(context.CancellationToken);
        if (content == null)
            return;

        var text = content.ToString();
        AnalyzeProjectFileContent(context, text, content, filePath);
    }

    private static void AnalyzeProjectFileContent(
        AdditionalFileAnalysisContext context,
        string text,
        SourceText sourceText,
        string filePath)
    {
        // Parse PackageReference elements
        // Look for: <PackageReference Include="PackageName" Version="X.Y.Z" />
        // or: <PackageReference Include="PackageName" Version="X.Y.Z">
        var lines = text.Split('\n');
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex].Trim();

            if (!line.Contains("PackageReference", StringComparison.OrdinalIgnoreCase))
                continue;

            var packageName = ExtractAttributeValue(line, "Include");
            var packageVersion = ExtractAttributeValue(line, "Version");

            if (packageName == null)
                continue;

            // LSG014: Check Microsoft.CodeAnalysis.CSharp version
            if (string.Equals(packageName, CodeAnalysisCSharpPackage, StringComparison.OrdinalIgnoreCase) &&
                packageVersion != null)
            {
                // Parse the version
                if (TryParseVersion(packageVersion, out var version) &&
                    version >= MaxRecommendedVersion)
                {
                    var lineSpan = GetLineSpan(sourceText, lineIndex, filePath);
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG014,
                        lineSpan,
                        packageVersion));
                }
            }

            // LSG012: Check for external dependencies
            bool isAllowed = false;
            foreach (var prefix in AllowedPackagePrefixes)
            {
                if (packageName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }

            if (!isAllowed)
            {
                var lineSpan = GetLineSpan(sourceText, lineIndex, filePath);
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG012,
                    lineSpan,
                    packageName));
            }
        }
    }

    private static string? ExtractAttributeValue(string line, string attributeName)
    {
        var pattern = attributeName + "=\"";
        var start = line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += pattern.Length;
        var end = line.IndexOf('"', start);
        if (end < 0)
            return null;

        return line.Substring(start, end - start);
    }

    private static bool TryParseVersion(string versionString, out Version version)
    {
        // Handle version strings with prefixes like "4.x.x" or "~4.x.x"
        var cleanVersion = versionString.TrimStart('~', '^', '[', '(');
        // Remove trailing range chars
        cleanVersion = cleanVersion.TrimEnd(']', ')', ',');

        return Version.TryParse(cleanVersion, out version!);
    }

    private static Location GetLineSpan(SourceText sourceText, int lineIndex, string filePath)
    {
        if (lineIndex < sourceText.Lines.Count)
        {
            var line = sourceText.Lines[lineIndex];
            return Location.Create(
                filePath,
                line.Span,
                new LinePositionSpan(
                    new LinePosition(lineIndex, 0),
                    new LinePosition(lineIndex, line.Span.Length)));
        }
        return Location.None;
    }
}
