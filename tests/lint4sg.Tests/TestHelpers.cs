using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
#pragma warning disable CS0618 // XUnitVerifier is obsolete but still functional
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace lint4sg.Tests;

/// <summary>
/// Base test infrastructure for analyzer tests.
/// Provides common stub code that mimics Microsoft.CodeAnalysis types
/// so that the analyzer's semantic model checks work correctly.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Stubs for Microsoft.CodeAnalysis types used by the generators.
    /// Including this in test code allows semantic model resolution.
    /// </summary>
    public const string RoslynStubs = """
        namespace Microsoft.CodeAnalysis
        {
            public interface ISourceGenerator
            {
                void Initialize(object context);
                void Execute(object context);
            }

            public interface IIncrementalGenerator
            {
                void Initialize(object context);
            }

            public class SyntaxValueProvider
            {
                public object CreateSyntaxProvider(
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, object> transform)
                    => null!;

                public object ForAttributeWithMetadataName(
                    string fullyQualifiedMetadataName,
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, object> transform)
                    => null!;
            }

            public class IncrementalGeneratorInitializationContext
            {
                public SyntaxValueProvider SyntaxProvider => null!;

                public void RegisterSourceOutput<TSource>(
                    object source,
                    System.Action<object, TSource> action) { }

                public void RegisterImplementationSourceOutput<TSource>(
                    object source,
                    System.Action<object, TSource> action) { }
            }

            public abstract class SyntaxNode
            {
                public SyntaxNode NormalizeWhitespace() => null!;
                public SyntaxNode NormalizeWhitespace(string indentation, bool elasticTrivia = false) => null!;
            }

            public interface ISymbol { }
            public interface INamedTypeSymbol : ISymbol { }
            public abstract class SemanticModel
            {
                public virtual ISymbol GetDeclaredSymbol(
                    object node,
                    System.Threading.CancellationToken cancellationToken = default)
                    => null!;
            }
            public abstract class Compilation { }
        }
        """;

    public const string GeneratorAttributeStub = """
        namespace Microsoft.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class GeneratorAttribute : System.Attribute { }
        }
        """;

    /// <summary>Creates an analyzer test with the standard stubs pre-included.</summary>
    public static CSharpAnalyzerTest<TAnalyzer, XUnitVerifier> CreateTest<TAnalyzer>(
        string sourceCode,
        params DiagnosticResult[] expectedDiagnostics)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, XUnitVerifier>
        {
            TestState =
            {
                Sources = { sourceCode, RoslynStubs, GeneratorAttributeStub },
            }
        };

        test.TestState.ExpectedDiagnostics.AddRange(expectedDiagnostics);
        return test;
    }
}
