using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG002: Warns when CreateSyntaxProvider is used instead of ForAttributeWithMetadataName.
/// LSG003: Errors when CreateSyntaxProvider performs expensive inheritance checks
/// without prior filtering, especially through GetDeclaredSymbol in the transform.
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

        // LSG003: Check for expensive inheritance/interface/attribute scans.
        // In the predicate these checks are always expensive because the predicate
        // runs on every syntax change. In the transform they are still not allowed
        // when CreateSyntaxProvider is being used to scan broadly via GetDeclaredSymbol
        // instead of pre-filtering (typically with ForAttributeWithMetadataName).
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count >= 1)
        {
            var predicateArg = arguments[0];
            if (ContainsExpensivePredicateCheck(predicateArg))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG003,
                    predicateArg.GetLocation()));
            }
        }

        if (arguments.Count >= 2)
        {
            var transformArg = arguments[1];
            if (ContainsExpensiveTransformScan(transformArg))
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

    private static bool ContainsExpensivePredicateCheck(SyntaxNode node) =>
        ContainsHighCostMemberAccess(node);

    private static bool ContainsExpensiveTransformScan(SyntaxNode node) =>
        ContainsGetDeclaredSymbolInvocation(node) && ContainsHighCostMemberAccess(node);

    private static bool ContainsGetDeclaredSymbolInvocation(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            if (descendant is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "GetDeclaredSymbol")
            {
                return true;
            }
        }

        return false;
    }
}
