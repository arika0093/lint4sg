using System.Reflection;
using Microsoft.CodeAnalysis;
using lint4sg.Analyzers;

namespace lint4sg.Tests;

public class DiagnosticDescriptorTests
{
    [Fact]
    public void AllLsgDescriptors_HaveGitHubReadmeHelpLinks()
    {
        var descriptorsType = typeof(ISourceGeneratorUsageAnalyzer).Assembly.GetType("lint4sg.DiagnosticDescriptors");

        Assert.NotNull(descriptorsType);

        var fields = descriptorsType!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.Name.StartsWith("LSG", StringComparison.Ordinal))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var descriptor = Assert.IsType<DiagnosticDescriptor>(field.GetValue(null));
            Assert.Equal($"https://github.com/arika0093/lint4sg#{descriptor.Id.ToLowerInvariant()}", descriptor.HelpLinkUri);
        }
    }
}
