using System.Threading.Tasks;
using lint4sg.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
#pragma warning disable CS0618
using Microsoft.CodeAnalysis.Testing.Verifiers;
#pragma warning restore CS0618

namespace lint4sg.Tests;

internal static class IncrementalPipelineAnalyzerTestHelpers
{
    public const string GenericTupleGuidance =
        "Flatten the model or introduce a named type.";
    public const string SameTypeTupleMergeGuidance =
        "Because matching Left and Right branches have the same type, merge them first with a helper such as MergeCollectedValues<T>(first, second).";

    public const string IncrementalStubs = """
        namespace Microsoft.CodeAnalysis
        {
            public interface IIncrementalGenerator
            {
                void Initialize(IncrementalGeneratorInitializationContext context);
            }

            public readonly struct IncrementalValueProvider<T> { }
            public readonly struct IncrementalValuesProvider<T> { }

            public sealed class SyntaxValueProvider
            {
                public IncrementalValuesProvider<TResult> CreateSyntaxProvider<TResult>(
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, TResult> transform)
                    => default;

                public IncrementalValuesProvider<TResult> ForAttributeWithMetadataName<TResult>(
                    string fullyQualifiedMetadataName,
                    System.Func<object, System.Threading.CancellationToken, bool> predicate,
                    System.Func<object, System.Threading.CancellationToken, TResult> transform)
                    => default;
            }

            public sealed class IncrementalGeneratorInitializationContext
            {
                public SyntaxValueProvider SyntaxProvider => null!;

                public void RegisterSourceOutput<TSource>(
                    IncrementalValueProvider<TSource> source,
                    System.Action<object, TSource> action) { }

                public void RegisterImplementationSourceOutput<TSource>(
                    IncrementalValueProvider<TSource> source,
                    System.Action<object, TSource> action) { }
            }

            public static class IncrementalProviderExtensions
            {
                public static IncrementalValuesProvider<TResult> Select<TSource, TResult>(
                    this IncrementalValuesProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, TResult> transform)
                    => default;

                public static IncrementalValueProvider<TResult> Select<TSource, TResult>(
                    this IncrementalValueProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, TResult> transform)
                    => default;

                public static IncrementalValuesProvider<TSource> Where<TSource>(
                    this IncrementalValuesProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, bool> predicate)
                    => default;

                public static IncrementalValueProvider<TSource> Where<TSource>(
                    this IncrementalValueProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, bool> predicate)
                    => default;

                public static IncrementalValueProvider<System.Collections.Immutable.ImmutableArray<TSource>> Collect<TSource>(
                    this IncrementalValuesProvider<TSource> source)
                    => default;

                public static IncrementalValuesProvider<TResult> SelectMany<TSource, TResult>(
                    this IncrementalValuesProvider<TSource> source,
                    System.Func<TSource, System.Threading.CancellationToken, System.Collections.Immutable.ImmutableArray<TResult>> selector)
                    => default;

                public static IncrementalValueProvider<(TLeft Left, TRight Right)> Combine<TLeft, TRight>(
                    this IncrementalValueProvider<TLeft> left,
                    IncrementalValueProvider<TRight> right)
                    => default;
            }
        }
        """;

    public static Task RunTestAsync(string code, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<IncrementalPipelineAnalyzer, XUnitVerifier>
        {
            TestState = { Sources = { code, IncrementalStubs } },
        };
        test.TestState.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }
}
