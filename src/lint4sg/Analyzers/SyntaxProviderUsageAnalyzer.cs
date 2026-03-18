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
        ImmutableArray.Create(
            DiagnosticDescriptors.LSG002,
            DiagnosticDescriptors.LSG003,
            DiagnosticDescriptors.LSG016);

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

        var isCreateSyntaxProvider = methodName == "CreateSyntaxProvider";
        var isForAttributeWithMetadataName = methodName == "ForAttributeWithMetadataName";
        if (!isCreateSyntaxProvider && !isForAttributeWithMetadataName)
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

        if (isCreateSyntaxProvider)
        {
            // LSG002: Warn about using CreateSyntaxProvider
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.LSG002,
                invocation.GetLocation()));
        }

        var arguments = invocation.ArgumentList.Arguments;
        var predicateArgumentIndex = isCreateSyntaxProvider ? 0 : 1;
        if (arguments.Count > predicateArgumentIndex)
        {
            var predicateArg = arguments[predicateArgumentIndex];

            foreach (var allocationNode in GetAllocationNodes(predicateArg))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG016,
                    allocationNode.GetLocation()));
            }

            // LSG003: Check for expensive inheritance/interface/attribute scans.
            // In the predicate these checks are always expensive because the predicate
            // runs on every syntax change. In the transform they are still not allowed
            // when CreateSyntaxProvider is being used to scan broadly via GetDeclaredSymbol
            // instead of pre-filtering (typically with ForAttributeWithMetadataName).
            if (isCreateSyntaxProvider && ContainsExpensivePredicateCheck(predicateArg))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG003,
                    predicateArg.GetLocation()));
            }
        }

        if (isCreateSyntaxProvider && arguments.Count >= 2)
        {
            var transformArg = arguments[1];
            if (ContainsExpensiveTransformScan(arguments[0], transformArg))
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

    private static bool ContainsExpensiveTransformScan(SyntaxNode predicate, SyntaxNode transform) =>
        IsBroadPredicate(predicate) &&
        ContainsGetDeclaredSymbolInvocation(transform) &&
        ContainsHighCostMemberAccess(transform);

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

    private static bool IsBroadPredicate(SyntaxNode node)
    {
        node = UnwrapNode(node);

        if (node is LambdaExpressionSyntax lambda)
        {
            return lambda.Body switch
            {
                BlockSyntax block => TryGetSingleReturnExpression(block) is { } expression && IsBroadPredicateExpression(expression),
                ExpressionSyntax expression => IsBroadPredicateExpression(expression),
                _ => false
            };
        }

        return IsBroadPredicateExpression(node);
    }

    private static bool IsBroadPredicateExpression(SyntaxNode node)
    {
        node = UnwrapNode(node);

        return node switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression) => true,
            IsPatternExpressionSyntax => true,
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.IsExpression) => true,
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) =>
                IsBroadPredicateExpression(binary.Left) && IsBroadPredicateExpression(binary.Right),
            ParenthesizedLambdaExpressionSyntax lambda => IsBroadPredicate(lambda),
            SimpleLambdaExpressionSyntax lambda => IsBroadPredicate(lambda),
            _ => false
        };
    }

    private static SyntaxNode UnwrapNode(SyntaxNode node)
    {
        while (true)
        {
            switch (node)
            {
                case ArgumentSyntax argument:
                    node = argument.Expression;
                    continue;
                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    continue;
                default:
                    return node;
            }
        }
    }

    private static ExpressionSyntax? TryGetSingleReturnExpression(BlockSyntax block)
    {
        if (block.Statements.Count == 1 && block.Statements[0] is ReturnStatementSyntax { Expression: { } returnExpression })
        {
            return returnExpression;
        }

        return null;
    }

    private static IEnumerable<SyntaxNode> GetAllocationNodes(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            switch (descendant)
            {
                case ObjectCreationExpressionSyntax:
                case ImplicitObjectCreationExpressionSyntax:
                case ArrayCreationExpressionSyntax:
                case ImplicitArrayCreationExpressionSyntax:
                case AnonymousObjectCreationExpressionSyntax:
                case CollectionExpressionSyntax:
                case InterpolatedStringExpressionSyntax:
                    yield return descendant;
                    break;
                case InvocationExpressionSyntax invocation when IsKnownAllocatingInvocation(invocation):
                    yield return invocation;
                    break;
            }
        }
    }

    private static bool IsKnownAllocatingInvocation(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };

        return name is "ToArray" or "ToList" or "ToDictionary" or "ToHashSet";
    }
}
