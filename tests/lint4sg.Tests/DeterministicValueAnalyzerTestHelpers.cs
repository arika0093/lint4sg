using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
#pragma warning disable CS0618
using Microsoft.CodeAnalysis.Testing.Verifiers;
#pragma warning restore CS0618

namespace lint4sg.Tests;

internal static class DeterministicValueAnalyzerTestHelpers
{
    public const string IncrementalStubs = """
        namespace Microsoft.CodeAnalysis
        {
            public interface ISymbol { }
            public abstract class SemanticModel { }
            public abstract class Compilation { }
            public abstract class SyntaxNode { }

            public struct IncrementalValueProvider<T> { }
            public struct IncrementalValuesProvider<T> { }

            public static class IncrementalProviderExtensions
            {
                public static IncrementalValuesProvider<TResult> Select<TSource, TResult>(
                    this IncrementalValuesProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, TResult> transform)
                    => default;
            }

            public class IncrementalGeneratorInitializationContext
            {
                public void RegisterSourceOutput<T>(
                    IncrementalValueProvider<T> source,
                    System.Action<object, T> action) { }

                public void RegisterImplementationSourceOutput<T>(
                    IncrementalValueProvider<T> source,
                    System.Action<object, T> action) { }
            }

            public class SyntaxValueProvider
            {
                public IncrementalValuesProvider<T> CreateSyntaxProvider<T>(
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, T> transform)
                    => default;

                public IncrementalValuesProvider<T> ForAttributeWithMetadataName<T>(
                    string fullyQualifiedMetadataName,
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, T> transform)
                    => default;
            }
        }
        """;

    public const string IsExternalInitStub = """
        namespace System.Runtime.CompilerServices
        {
            internal static class IsExternalInit { }
        }
        """;

    public static Task RunTestAsync(string code, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<DeterministicValueAnalyzer, XUnitVerifier>
        {
            TestState = { Sources = { code, IncrementalStubs, IsExternalInitStub } },
        };
        test.TestState.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }
}
