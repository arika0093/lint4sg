using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG001: Detects usage of ISourceGenerator. IIncrementalGenerator should be used instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ISourceGeneratorUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.LSG001);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBaseList, SyntaxKind.SimpleBaseType);
    }

    private static void AnalyzeBaseList(SyntaxNodeAnalysisContext context)
    {
        var baseType = (SimpleBaseTypeSyntax)context.Node;
        var typeName = baseType.Type;

        // Check if the base type is ISourceGenerator
        var symbol = context.SemanticModel.GetSymbolInfo(typeName).Symbol;
        if (symbol is INamedTypeSymbol namedType &&
            namedType.Name == "ISourceGenerator" &&
            namedType.ContainingNamespace?.ToString() == "Microsoft.CodeAnalysis")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.LSG001,
                baseType.GetLocation()));
        }
    }
}
