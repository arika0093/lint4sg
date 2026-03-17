using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG002: Warns when CreateSyntaxProvider is used instead of ForAttributeWithMetadataName.
/// LSG003: Errors when CreateSyntaxProvider predicate uses expensive inheritance checks.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyntaxProviderUsageAnalyzer : DiagnosticAnalyzer
{
    // High-cost member access names that indicate inheritance/interface checking
    private static readonly ImmutableHashSet<string> HighCostMemberNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Interfaces",
        "AllInterfaces",
        "BaseType",
        "IsAssignableTo",
        "IsAssignableFrom",
        "BaseList",
        "GetAttributes"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.LSG002, DiagnosticDescriptors.LSG003);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a SyntaxProvider method call
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;

        // We want to detect SyntaxProvider.CreateSyntaxProvider(...) calls
        // FAWMN is ForAttributeWithMetadataName - not flagged for LSG002
        if (methodName != "CreateSyntaxProvider")
            return;

        // Verify this is on a SyntaxValueProvider via semantic model
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
        if (methodSymbol == null)
        {
            // Try with candidate symbols
            if (symbolInfo.CandidateSymbols.Length > 0)
                methodSymbol = symbolInfo.CandidateSymbols[0] as IMethodSymbol;
        }

        if (methodSymbol != null)
        {
            var containingType = methodSymbol.ContainingType;
            // Check that this is Microsoft.CodeAnalysis.SyntaxValueProvider
            if (containingType?.Name != "SyntaxValueProvider" ||
                containingType.ContainingNamespace?.ToString() != "Microsoft.CodeAnalysis")
            {
                return;
            }
        }
        else
        {
            // If we can't resolve the symbol, check the name heuristically
            // by checking if the receiver looks like a SyntaxProvider
            if (!IsSyntaxProviderAccess(memberAccess))
                return;
        }

        // LSG002: Warn about using CreateSyntaxProvider
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.LSG002,
            invocation.GetLocation()));

        // LSG003: Check if the predicate uses high-cost inheritance checks
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count >= 1)
        {
            var predicateArg = arguments[0];
            if (ContainsHighCostMemberAccess(predicateArg))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG003,
                    predicateArg.GetLocation()));
            }
        }

        // Also check the transform argument (second parameter) for LSG003
        if (arguments.Count >= 2)
        {
            var transformArg = arguments[1];
            if (ContainsHighCostMemberAccess(transformArg))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG003,
                    transformArg.GetLocation()));
            }
        }
    }

    private static bool IsSyntaxProviderAccess(MemberAccessExpressionSyntax memberAccess)
    {
        // Check if receiver contains "SyntaxProvider" somewhere in the chain
        var receiverText = memberAccess.Expression.ToString();
        return receiverText.Contains("SyntaxProvider") || receiverText.Contains("syntaxProvider");
    }

    private static bool ContainsHighCostMemberAccess(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodes())
        {
            if (descendant is MemberAccessExpressionSyntax memberAccess)
            {
                var name = memberAccess.Name.Identifier.Text;
                if (HighCostMemberNames.Contains(name))
                    return true;
            }
            else if (descendant is IdentifierNameSyntax identifier)
            {
                if (HighCostMemberNames.Contains(identifier.Identifier.Text))
                    return true;
            }
        }
        return false;
    }
}
