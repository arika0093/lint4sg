using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
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
        // Parse the project file as XML so that multi-line PackageReference elements
        // (where attributes or child elements span multiple lines) are handled correctly.
        XDocument doc;
        try
        {
            doc = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            // Not valid XML – skip analysis rather than crashing.
            return;
        }

        foreach (var element in doc.Descendants("PackageReference"))
        {
            var packageName = (string?)element.Attribute("Include");
            if (packageName == null)
                continue;

            // Version may be an XML attribute OR a child element, e.g.:
            //   <PackageReference Include="Foo" Version="1.0" />
            //   <PackageReference Include="Foo"><Version>1.0</Version></PackageReference>
            var packageVersion = (string?)element.Attribute("Version")
                                 ?? element.Element("Version")?.Value;
            var privateAssets = (string?)element.Attribute("PrivateAssets")
                                ?? element.Element("PrivateAssets")?.Value;

            // Map the element's start line back to a SourceText span for diagnostics.
            var lineInfo = element as IXmlLineInfo;
            var lineIndex = lineInfo?.HasLineInfo() == true ? lineInfo.LineNumber - 1 : 0;
            var location = GetLineSpan(sourceText, lineIndex, filePath);

            // LSG014: Microsoft.CodeAnalysis.CSharp version >= 5.0.0
            if (string.Equals(packageName, CodeAnalysisCSharpPackage, StringComparison.OrdinalIgnoreCase)
                && packageVersion != null)
            {
                if (TryParseVersion(packageVersion, out var version) && version >= MaxRecommendedVersion)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG014,
                        location,
                        packageVersion));
                }
            }

            // LSG012: External (non-CodeAnalysis) NuGet package
            var isAllowed = false;
            foreach (var prefix in AllowedPackagePrefixes)
            {
                if (packageName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }

            if (!isAllowed && !ContainsAllPrivateAssets(privateAssets))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG012,
                    location,
                    packageName));
            }
        }
    }

    private static bool ContainsAllPrivateAssets(string? privateAssets)
    {
        if (string.IsNullOrWhiteSpace(privateAssets))
            return false;

        return privateAssets
            .Split(new[] { ';', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(static token => string.Equals(token, "all", StringComparison.OrdinalIgnoreCase));
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
