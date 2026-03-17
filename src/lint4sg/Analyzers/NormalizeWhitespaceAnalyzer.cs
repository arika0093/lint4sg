using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG009: NormalizeWhitespace() method call detected.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NormalizeWhitespaceAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.LSG009);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "NormalizeWhitespace")
            return;

        // Verify via semantic model that this is a SyntaxNode.NormalizeWhitespace method
        var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol == null)
        {
            // If symbol can't be resolved, skip (avoid false positives for user-defined methods)
            return;
        }

        // Check if it's from Microsoft.CodeAnalysis namespace (SyntaxNode.NormalizeWhitespace)
        var containingType = symbol.ContainingType;
        if (containingType != null)
        {
            var ns = containingType.ContainingNamespace?.ToString();
            if (ns != null && ns.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG009,
                    invocation.GetLocation()));
            }
        }
    }
}
