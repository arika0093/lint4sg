using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG004: CancellationToken must be forwarded to all methods that accept it.
/// LSG005: ThrowIfCancellationRequested must be called in each loop when CT is available.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationTokenAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.LSG004, DiagnosticDescriptors.LSG005);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.AnonymousMethodExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.SimpleLambdaExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.ParenthesizedLambdaExpression);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        // Get the cancellation token parameters for this method
        var ctParameters = GetCancellationTokenParameters(context.Node, context.SemanticModel);
        if (ctParameters.Count == 0)
            return;

        var body = GetMethodBody(context.Node);
        if (body == null)
            return;

        // LSG004: Check for method invocations that accept CancellationToken but don't receive it,
        // and for non-trivial project methods that should accept CancellationToken so that it can
        // continue flowing through the call chain.
        AnalyzeCancellationTokenForwarding(
            context,
            body,
            ctParameters,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));

        // LSG005: Check for loops without ThrowIfCancellationRequested
        AnalyzeLoopsForThrowIfCancelled(context, body, ctParameters);
    }

    private static ImmutableList<string> GetCancellationTokenParameters(SyntaxNode node, SemanticModel semanticModel)
    {
        var parameterList = node switch
        {
            MethodDeclarationSyntax m => m.ParameterList,
            LocalFunctionStatementSyntax lf => lf.ParameterList,
            AnonymousMethodExpressionSyntax am => am.ParameterList,
            ParenthesizedLambdaExpressionSyntax pl => pl.ParameterList,
            _ => null
        };

        if (parameterList == null)
            return ImmutableList<string>.Empty;

        var ctParams = ImmutableList.CreateBuilder<string>();
        foreach (var param in parameterList.Parameters)
        {
            ITypeSymbol? typeSymbol;
            if (param.Type != null)
            {
                typeSymbol = semanticModel.GetSymbolInfo(param.Type).Symbol as ITypeSymbol;
            }
            else
            {
                // Inferred lambda parameter type (common in `(ctx, ct) => ...` callbacks).
                // GetDeclaredSymbol resolves the parameter symbol whose Type reflects the
                // delegate's signature even when no explicit type annotation is written.
                var paramSymbol = semanticModel.GetDeclaredSymbol(param) as IParameterSymbol;
                typeSymbol = paramSymbol?.Type;
            }

            if (typeSymbol != null && IsCancellationToken(typeSymbol))
            {
                ctParams.Add(param.Identifier.Text);
            }
        }
        return ctParams.ToImmutable();
    }

    private static SyntaxNode? GetMethodBody(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
            LocalFunctionStatementSyntax lf => (SyntaxNode?)lf.Body ?? lf.ExpressionBody,
            AnonymousMethodExpressionSyntax am => am.Body,
            ParenthesizedLambdaExpressionSyntax pl => pl.Body,
            SimpleLambdaExpressionSyntax sl => sl.Body,
            _ => null
        };
    }

    private static void AnalyzeCancellationTokenForwarding(
        SyntaxNodeAnalysisContext context,
        SyntaxNode body,
        ImmutableList<string> ctParameters,
        HashSet<IMethodSymbol> reportedMissingTokenMethods)
    {
        // Find all invocations in the body (but not inside nested methods)
        foreach (var invocation in GetDirectInvocations(body))
        {
            var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
            var methodSymbol = ResolveMethodSymbol(semanticModel, invocation);
            if (methodSymbol == null)
                continue;

            // Check if this method has a CancellationToken parameter
            var ctParamIndex = FindCancellationTokenParameter(methodSymbol);
            if (ctParamIndex >= 0)
            {
                // Check if the CancellationToken is being passed in this invocation
                if (!IsCancellationTokenPassed(invocation, ctParamIndex, ctParameters))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG004,
                        invocation.GetLocation(),
                        methodSymbol.Name));
                }
                continue;
            }

            if (!TryGetOwnMethodBody(methodSymbol, out var calleeBody, out var calleeLocation))
                continue;

            if (IsContractBoundMethod(methodSymbol))
                continue;

            if (!RequiresCancellationTokenParameter(context.SemanticModel.Compilation, calleeBody))
                continue;

            if (!reportedMissingTokenMethods.Add(methodSymbol.OriginalDefinition))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.LSG004,
                calleeLocation,
                methodSymbol.Name));

            AnalyzeNestedOwnMethodsWithoutCancellationToken(
                context,
                calleeBody,
                reportedMissingTokenMethods);
        }
    }

    private static void AnalyzeNestedOwnMethodsWithoutCancellationToken(
        SyntaxNodeAnalysisContext context,
        SyntaxNode body,
        HashSet<IMethodSymbol> reportedMissingTokenMethods)
    {
        foreach (var invocation in GetDirectInvocations(body))
        {
            var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
            var methodSymbol = ResolveMethodSymbol(semanticModel, invocation);
            if (methodSymbol == null || FindCancellationTokenParameter(methodSymbol) >= 0)
                continue;

            if (!TryGetOwnMethodBody(methodSymbol, out var calleeBody, out var calleeLocation))
                continue;

            if (IsContractBoundMethod(methodSymbol))
                continue;

            if (!RequiresCancellationTokenParameter(context.SemanticModel.Compilation, calleeBody))
                continue;

            if (!reportedMissingTokenMethods.Add(methodSymbol.OriginalDefinition))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.LSG004,
                calleeLocation,
                methodSymbol.Name));

            AnalyzeNestedOwnMethodsWithoutCancellationToken(
                context,
                calleeBody,
                reportedMissingTokenMethods);
        }
    }

    private static void AnalyzeLoopsForThrowIfCancelled(
        SyntaxNodeAnalysisContext context,
        SyntaxNode body,
        ImmutableList<string> ctParameters)
    {
        foreach (var loop in GetDirectLoops(body))
        {
            var loopBody = GetLoopBody(loop);
            if (loopBody == null)
                continue;

            // Check if ThrowIfCancellationRequested is called in this loop body
            if (!ContainsThrowIfCancelled(loopBody, ctParameters))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG005,
                    loop.GetLocation()));
            }
        }
    }

    private static IEnumerable<InvocationExpressionSyntax> GetDirectInvocations(SyntaxNode body)
    {
        // Get invocations that are not inside nested lambdas/local functions
        foreach (var node in body.ChildNodes())
        {
            foreach (var invocation in GetDirectInvocationsRecursive(node))
                yield return invocation;
        }
    }

    private static IEnumerable<InvocationExpressionSyntax> GetDirectInvocationsRecursive(SyntaxNode node)
    {
        // Stop at nested function boundaries
        if (node is AnonymousMethodExpressionSyntax ||
            node is SimpleLambdaExpressionSyntax ||
            node is ParenthesizedLambdaExpressionSyntax ||
            node is LocalFunctionStatementSyntax)
        {
            yield break;
        }

        if (node is InvocationExpressionSyntax invocation)
            yield return invocation;

        foreach (var child in node.ChildNodes())
        {
            foreach (var result in GetDirectInvocationsRecursive(child))
                yield return result;
        }
    }

    private static IEnumerable<SyntaxNode> GetDirectLoops(SyntaxNode body)
    {
        foreach (var node in body.DescendantNodes(n =>
            n == body ||
            !(n is AnonymousMethodExpressionSyntax ||
              n is SimpleLambdaExpressionSyntax ||
              n is ParenthesizedLambdaExpressionSyntax ||
              n is LocalFunctionStatementSyntax)))
        {
            if (node is ForStatementSyntax ||
                node is ForEachStatementSyntax ||
                node is WhileStatementSyntax ||
                node is DoStatementSyntax)
            {
                yield return node;
            }
        }
    }

    private static SyntaxNode? GetLoopBody(SyntaxNode loop)
    {
        return loop switch
        {
            ForStatementSyntax f => f.Statement,
            ForEachStatementSyntax fe => fe.Statement,
            WhileStatementSyntax w => w.Statement,
            DoStatementSyntax d => d.Statement,
            _ => null
        };
    }

    private static bool ContainsThrowIfCancelled(SyntaxNode loopBody, ImmutableList<string> ctParameters)
    {
        foreach (var invocation in loopBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Name.Identifier.Text == "ThrowIfCancellationRequested")
                {
                    // Check that it's called on one of the CT parameters
                    var receiver = memberAccess.Expression.ToString();
                    if (ctParameters.Any(p => receiver.Contains(p)))
                        return true;
                    // Also accept if it's just ThrowIfCancellationRequested on any CT
                    return true;
                }
            }
        }
        return false;
    }

    private static int FindCancellationTokenParameter(IMethodSymbol method)
    {
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (IsCancellationToken(method.Parameters[i].Type))
                return i;
        }
        return -1;
    }

    private static IMethodSymbol? ResolveMethodSymbol(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
            return methodSymbol;

        foreach (var candidate in symbolInfo.CandidateSymbols)
        {
            if (candidate is IMethodSymbol candidateMethod)
                return candidateMethod;
        }

        return null;
    }

    private static bool TryGetOwnMethodBody(
        IMethodSymbol methodSymbol,
        out SyntaxNode body,
        out Location location)
    {
        foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
        {
            var declaration = syntaxReference.GetSyntax();
            if (declaration is not (MethodDeclarationSyntax or LocalFunctionStatementSyntax))
                continue;

            var methodBody = GetMethodBody(declaration);
            if (methodBody == null)
                continue;

            body = methodBody;
            location = GetMethodLocation(declaration);
            return true;
        }

        body = null!;
        location = Location.None;
        return false;
    }

    private static Location GetMethodLocation(SyntaxNode declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.GetLocation(),
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.GetLocation(),
            _ => declaration.GetLocation()
        };
    }

    private static bool RequiresCancellationTokenParameter(
        Compilation compilation,
        SyntaxNode body)
    {
        return GetBodyLineCount(body) >= 5 || ContainsOwnMethodInvocation(compilation, body);
    }

    private static bool IsContractBoundMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.IsOverride ||
               methodSymbol.ExplicitInterfaceImplementations.Length > 0;
    }

    private static int GetBodyLineCount(SyntaxNode body)
    {
        var lineSpan = body.SyntaxTree.GetLineSpan(body.Span);
        return lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;
    }

    private static bool ContainsOwnMethodInvocation(Compilation compilation, SyntaxNode body)
    {
        foreach (var invocation in GetDirectInvocations(body))
        {
            var semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
            var methodSymbol = ResolveMethodSymbol(semanticModel, invocation);
            if (methodSymbol?.DeclaringSyntaxReferences.Length > 0)
                return true;
        }

        return false;
    }

    private static bool IsCancellationTokenPassed(
        InvocationExpressionSyntax invocation,
        int ctParamIndex,
        ImmutableList<string> ctParameters)
    {
        var args = invocation.ArgumentList.Arguments;

        // Check named arguments for CancellationToken
        foreach (var arg in args)
        {
            if (arg.NameColon?.Name.Identifier.Text == "cancellationToken" ||
                arg.NameColon?.Name.Identifier.Text == "ct" ||
                arg.NameColon?.Name.Identifier.Text == "token")
            {
                return true;
            }

            // Check if any argument is a cancellation token variable
            var argText = arg.Expression.ToString();
            if (ctParameters.Any(p => argText == p || argText.Contains(p)))
                return true;
        }

        // Check positional argument
        if (ctParamIndex < args.Count)
        {
            var argText = args[ctParamIndex].Expression.ToString();
            return ctParameters.Any(p => argText == p || argText.Contains(p));
        }

        // Also check if CancellationToken.None or default is being passed - that's acceptable
        foreach (var arg in args)
        {
            var argText = arg.Expression.ToString();
            if (argText == "CancellationToken.None" || argText == "default" || argText == "default(CancellationToken)")
                return true;
        }

        return false;
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.Name == "CancellationToken" &&
               (type.ContainingNamespace?.ToString() == "System.Threading" ||
                type.ContainingNamespace?.ToString() == "System.Threading.CancellationToken");
    }
}
