using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG004: Add CancellationToken to project helpers in source-generator call trees that reach cancellation-aware work.
/// LSG005: Once a CancellationToken is available, pass it to child calls and check it inside loops.
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
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var methods = CollectProjectMethods(context.Compilation);
        if (methods.Count == 0)
            return;

        var roots = FindSourceGeneratorRoots(context.Compilation, methods);
        if (roots.IsDefaultOrEmpty)
            return;

        var state = new AnalysisState(context.Compilation, methods);
        foreach (var root in roots)
        {
            AnalyzeUsageDiagnostics(context, state, root.Body, root.SemanticModel, root.CancellationTokenParameters);

            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var child in GetProjectCallees(root.Body, context.Compilation, methods))
            {
                TraverseProjectMethod(context, state, child, visited);
            }
        }
    }

    private static Dictionary<IMethodSymbol, MethodContext> CollectProjectMethods(Compilation compilation)
    {
        var methods = new Dictionary<IMethodSymbol, MethodContext>(SymbolEqualityComparer.Default);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var declaration in root.DescendantNodes().Where(static node =>
                node is MethodDeclarationSyntax or LocalFunctionStatementSyntax))
            {
                var symbol = semanticModel.GetDeclaredSymbol(declaration) as IMethodSymbol;
                var body = GetMethodBody(declaration);
                if (symbol == null || body == null || !IsProjectMethod(symbol))
                    continue;

                methods[symbol.OriginalDefinition] = new MethodContext(
                    symbol.OriginalDefinition,
                    body,
                    semanticModel,
                    GetMethodLocation(declaration),
                    GetCancellationTokenParameters(declaration, semanticModel));
            }
        }

        return methods;
    }

    private static ImmutableArray<BodyContext> FindSourceGeneratorRoots(
        Compilation compilation,
        Dictionary<IMethodSymbol, MethodContext> methods)
    {
        var roots = ImmutableArray.CreateBuilder<BodyContext>();
        var seen = new HashSet<string>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var invokedMethod = ResolveMethodSymbol(semanticModel, invocation);
                if (invokedMethod == null || !IsSourceGeneratorCancellationCallback(invokedMethod))
                    continue;

                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    var parameter = (semanticModel.GetOperation(argument) as IArgumentOperation)?.Parameter;
                    if (parameter == null || !DelegateAcceptsCancellationToken(parameter.Type))
                        continue;

                    if (TryCreateBodyContext(argument.Expression, semanticModel, methods, out var bodyContext))
                    {
                        var key = GetLocationKey(bodyContext.Location);
                        if (seen.Add(key))
                        {
                            roots.Add(bodyContext);
                        }
                    }
                }
            }
        }

        return roots.ToImmutable();
    }

    private static bool TryCreateBodyContext(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        Dictionary<IMethodSymbol, MethodContext> methods,
        out BodyContext bodyContext)
    {
        var body = GetMethodBody(expression);
        if (body != null)
        {
            var ctParameters = GetCancellationTokenParameters(expression, semanticModel);
            if (ctParameters.Count > 0)
            {
                bodyContext = new BodyContext(body, semanticModel, expression.GetLocation(), ctParameters);
                return true;
            }
        }

        if (semanticModel.GetSymbolInfo(expression).Symbol is IMethodSymbol methodSymbol &&
            methods.TryGetValue(methodSymbol.OriginalDefinition, out var methodContext) &&
            methodContext.CancellationTokenParameters.Count > 0)
        {
            bodyContext = new BodyContext(
                methodContext.Body,
                methodContext.SemanticModel,
                methodContext.Location,
                methodContext.CancellationTokenParameters);
            return true;
        }

        bodyContext = default;
        return false;
    }

    private static void TraverseProjectMethod(
        CompilationAnalysisContext context,
        AnalysisState state,
        MethodContext method,
        HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(method.Symbol))
            return;

        if (!RequiresCancellationToken(state, method))
            return;

        if (!method.HasCancellationTokenParameter)
        {
            if (HasCancellationTokenOverload(method.Symbol))
                return;

            if (!IsContractBoundMethod(method.Symbol) && state.ReportedLsg004.Add(method.Symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG004,
                    method.Location,
                    method.Symbol.Name));
            }
        }
        else
        {
            AnalyzeUsageDiagnostics(
                context,
                state,
                method.Body,
                method.SemanticModel,
                method.CancellationTokenParameters);
        }

        foreach (var child in GetProjectCallees(method.Body, state.Compilation, state.Methods))
        {
            TraverseProjectMethod(context, state, child, visited);
        }
    }

    private static bool RequiresCancellationToken(AnalysisState state, MethodContext method)
    {
        if (state.RequirementCache.TryGetValue(method.Symbol, out var cached))
            return cached;

        if (!state.RequirementStack.Add(method.Symbol))
            return false;

        var requiresCancellationToken =
            GetDirectLoops(method.Body).Any() ||
            ContainsCancellationAwareInvocation(method.Body, state.Compilation);

        foreach (var child in GetProjectCallees(method.Body, state.Compilation, state.Methods))
        {
            if (RequiresCancellationToken(state, child))
            {
                requiresCancellationToken = true;
            }
        }

        state.RequirementStack.Remove(method.Symbol);
        state.RequirementCache[method.Symbol] = requiresCancellationToken;
        return requiresCancellationToken;
    }

    private static void AnalyzeUsageDiagnostics(
        CompilationAnalysisContext context,
        AnalysisState state,
        SyntaxNode body,
        SemanticModel semanticModel,
        ImmutableList<string> cancellationTokenParameters)
    {
        foreach (var invocation in GetDirectInvocations(body))
        {
            var invocationSemanticModel = state.Compilation.GetSemanticModel(invocation.SyntaxTree);
            var methodSymbol = ResolveMethodSymbol(invocationSemanticModel, invocation);
            if (methodSymbol == null)
                continue;

            if (state.Methods.TryGetValue(methodSymbol.OriginalDefinition, out var childMethod))
            {
                var ctParameterIndex = FindCancellationTokenParameter(methodSymbol);
                if (ctParameterIndex < 0)
                {
                    if (HasCancellationTokenOverload(methodSymbol))
                    {
                        ReportUsageDiagnostic(context, state, invocation.GetLocation());
                    }

                    continue;
                }

                if (!RequiresCancellationToken(state, childMethod))
                    continue;

                if (!IsCancellationTokenPassed(
                    invocationSemanticModel,
                    invocation,
                    methodSymbol,
                    cancellationTokenParameters))
                {
                    ReportUsageDiagnostic(context, state, invocation.GetLocation());
                }

                continue;
            }

            if (!SupportsCancellationToken(methodSymbol))
                continue;

            if (!IsCancellationTokenPassed(
                invocationSemanticModel,
                invocation,
                methodSymbol,
                cancellationTokenParameters))
            {
                ReportUsageDiagnostic(context, state, invocation.GetLocation());
            }
        }

        foreach (var loop in GetDirectLoops(body))
        {
            var loopBody = GetLoopBody(loop);
            if (loopBody == null || ContainsThrowIfCancelled(loopBody, cancellationTokenParameters))
                continue;

            ReportUsageDiagnostic(context, state, loop.GetLocation());
        }
    }

    private static void ReportUsageDiagnostic(
        CompilationAnalysisContext context,
        AnalysisState state,
        Location location)
    {
        var key = GetLocationKey(location);
        if (state.ReportedLsg005Locations.Add(key))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.LSG005,
                location));
        }
    }

    private static bool ContainsCancellationAwareInvocation(
        SyntaxNode body,
        Compilation compilation)
    {
        foreach (var invocation in GetDirectInvocations(body))
        {
            var semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
            var methodSymbol = ResolveMethodSymbol(semanticModel, invocation);
            if (methodSymbol == null)
                continue;

            if (SupportsCancellationToken(methodSymbol))
                return true;
        }

        return false;
    }

    private static IEnumerable<MethodContext> GetProjectCallees(
        SyntaxNode body,
        Compilation compilation,
        Dictionary<IMethodSymbol, MethodContext> methods)
    {
        foreach (var invocation in GetDirectInvocations(body))
        {
            var semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
            var methodSymbol = ResolveMethodSymbol(semanticModel, invocation);
            if (methodSymbol == null)
                continue;

            if (methods.TryGetValue(methodSymbol.OriginalDefinition, out var methodContext))
            {
                yield return methodContext;
            }
        }
    }

    private static bool SupportsCancellationToken(IMethodSymbol methodSymbol)
    {
        return FindCancellationTokenParameter(methodSymbol) >= 0 ||
               HasCancellationTokenOverload(methodSymbol);
    }

    private static bool HasCancellationTokenOverload(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return false;

        var baseSignature = methodSymbol.Parameters
            .Where(static parameter => !IsCancellationToken(parameter.Type))
            .Select(static parameter => parameter.Type)
            .ToArray();

        foreach (var overload in containingType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>())
        {
            if (SymbolEqualityComparer.Default.Equals(overload, methodSymbol))
                continue;

            if (FindCancellationTokenParameter(overload) < 0)
                continue;

            var overloadSignature = overload.Parameters
                .Where(static parameter => !IsCancellationToken(parameter.Type))
                .Select(static parameter => parameter.Type)
                .ToArray();

            if (baseSignature.Length != overloadSignature.Length)
                continue;

            var matches = true;
            for (var i = 0; i < baseSignature.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(baseSignature[i], overloadSignature[i]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static bool IsSourceGeneratorCancellationCallback(IMethodSymbol methodSymbol)
    {
        var containingNamespace = methodSymbol.ContainingNamespace?.ToDisplayString();
        if (containingNamespace == null ||
            !containingNamespace.StartsWith("Microsoft.CodeAnalysis", System.StringComparison.Ordinal))
        {
            return false;
        }

        return methodSymbol.Parameters.Any(static parameter => DelegateAcceptsCancellationToken(parameter.Type));
    }

    private static bool DelegateAcceptsCancellationToken(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType || namedType.TypeKind != TypeKind.Delegate)
            return false;

        return namedType.DelegateInvokeMethod?.Parameters.Any(static parameter => IsCancellationToken(parameter.Type)) == true;
    }

    private static ImmutableList<string> GetCancellationTokenParameters(SyntaxNode node, SemanticModel semanticModel)
    {
        IEnumerable<ParameterSyntax>? parameters = node switch
        {
            MethodDeclarationSyntax method => method.ParameterList.Parameters,
            LocalFunctionStatementSyntax localFunction => localFunction.ParameterList.Parameters,
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.ParameterList?.Parameters,
            ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters,
            SimpleLambdaExpressionSyntax simpleLambda => new ParameterSyntax[] { simpleLambda.Parameter },
            _ => null,
        };

        if (parameters == null)
            return ImmutableList<string>.Empty;

        var ctParameters = ImmutableList.CreateBuilder<string>();
        foreach (var parameter in parameters)
        {
            ITypeSymbol? typeSymbol;
            if (parameter.Type != null)
            {
                typeSymbol = semanticModel.GetSymbolInfo(parameter.Type).Symbol as ITypeSymbol;
            }
            else
            {
                var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter) as IParameterSymbol;
                typeSymbol = parameterSymbol?.Type;
            }

            if (typeSymbol != null && IsCancellationToken(typeSymbol))
            {
                ctParameters.Add(parameter.Identifier.Text);
            }
        }

        return ctParameters.ToImmutable();
    }

    private static SyntaxNode? GetMethodBody(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
            LocalFunctionStatementSyntax localFunction => (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody,
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.Body,
            ParenthesizedLambdaExpressionSyntax lambda => lambda.Body,
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Body,
            _ => null,
        };
    }

    private static IEnumerable<InvocationExpressionSyntax> GetDirectInvocations(SyntaxNode body)
    {
        if (body is InvocationExpressionSyntax invocationBody)
        {
            yield return invocationBody;
            yield break;
        }

        foreach (var node in body.ChildNodes())
        {
            foreach (var invocation in GetDirectInvocationsRecursive(node))
                yield return invocation;
        }
    }

    private static IEnumerable<InvocationExpressionSyntax> GetDirectInvocationsRecursive(SyntaxNode node)
    {
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
        foreach (var node in body.DescendantNodes(node =>
            node == body ||
            !(node is AnonymousMethodExpressionSyntax ||
              node is SimpleLambdaExpressionSyntax ||
              node is ParenthesizedLambdaExpressionSyntax ||
              node is LocalFunctionStatementSyntax)))
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
            ForStatementSyntax forStatement => forStatement.Statement,
            ForEachStatementSyntax forEachStatement => forEachStatement.Statement,
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            _ => null,
        };
    }

    private static bool ContainsThrowIfCancelled(SyntaxNode loopBody, ImmutableList<string> ctParameters)
    {
        foreach (var invocation in GetDirectInvocations(loopBody))
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            if (memberAccess.Name.Identifier.Text != "ThrowIfCancellationRequested")
                continue;

            if (ReferencesCancellationToken(memberAccess.Expression, ctParameters))
                return true;
        }

        return false;
    }

    private static int FindCancellationTokenParameter(IMethodSymbol method)
    {
        for (var index = 0; index < method.Parameters.Length; index++)
        {
            if (IsCancellationToken(method.Parameters[index].Type))
                return index;
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

    private static Location GetMethodLocation(SyntaxNode declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.GetLocation(),
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.GetLocation(),
            _ => declaration.GetLocation(),
        };
    }

    private static bool IsContractBoundMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.IsOverride ||
               methodSymbol.ExplicitInterfaceImplementations.Length > 0;
    }

    private static bool IsCancellationTokenPassed(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        ImmutableList<string> ctParameters)
    {
        var invocationOperation = semanticModel.GetOperation(invocation) as IInvocationOperation;
        if (invocationOperation != null)
        {
            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.Parameter != null &&
                    IsCancellationToken(argument.Parameter.Type) &&
                    ReferencesCancellationToken(argument.Value.Syntax, ctParameters))
                {
                    return true;
                }
            }

            return false;
        }

        var ctParameterIndex = FindCancellationTokenParameter(methodSymbol);
        return ctParameterIndex >= 0 &&
               ctParameterIndex < invocation.ArgumentList.Arguments.Count &&
               ReferencesCancellationToken(
                   invocation.ArgumentList.Arguments[ctParameterIndex].Expression,
                   ctParameters);
    }

    private static bool ReferencesCancellationToken(
        SyntaxNode expression,
        ImmutableList<string> ctParameters)
    {
        return expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => ctParameters.Contains(identifier.Identifier.Text));
    }

    private static bool IsProjectMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.DeclaringSyntaxReferences.Length == 0)
            return false;

        var containingNamespace = methodSymbol.ContainingNamespace?.ToDisplayString();
        return containingNamespace == null ||
               (!containingNamespace.StartsWith("System", System.StringComparison.Ordinal) &&
                !containingNamespace.StartsWith("Microsoft.CodeAnalysis", System.StringComparison.Ordinal));
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToDisplayString().StartsWith("System.Threading", System.StringComparison.Ordinal) == true;
    }

    private static string GetLocationKey(Location location)
    {
        return $"{location.SourceTree?.FilePath}:{location.SourceSpan.Start}:{location.SourceSpan.Length}";
    }

    private readonly record struct BodyContext(
        SyntaxNode Body,
        SemanticModel SemanticModel,
        Location Location,
        ImmutableList<string> CancellationTokenParameters);

    private sealed class MethodContext
    {
        public MethodContext(
            IMethodSymbol symbol,
            SyntaxNode body,
            SemanticModel semanticModel,
            Location location,
            ImmutableList<string> cancellationTokenParameters)
        {
            Symbol = symbol;
            Body = body;
            SemanticModel = semanticModel;
            Location = location;
            CancellationTokenParameters = cancellationTokenParameters;
        }

        public IMethodSymbol Symbol { get; }
        public SyntaxNode Body { get; }
        public SemanticModel SemanticModel { get; }
        public Location Location { get; }
        public ImmutableList<string> CancellationTokenParameters { get; }
        public bool HasCancellationTokenParameter => CancellationTokenParameters.Count > 0;
    }

    private sealed class AnalysisState
    {
        public AnalysisState(
            Compilation compilation,
            Dictionary<IMethodSymbol, MethodContext> methods)
        {
            Compilation = compilation;
            Methods = methods;
        }

        public Compilation Compilation { get; }
        public Dictionary<IMethodSymbol, MethodContext> Methods { get; }
        public Dictionary<IMethodSymbol, bool> RequirementCache { get; } =
            new(SymbolEqualityComparer.Default);
        public HashSet<IMethodSymbol> RequirementStack { get; } =
            new(SymbolEqualityComparer.Default);
        public HashSet<IMethodSymbol> ReportedLsg004 { get; } =
            new(SymbolEqualityComparer.Default);
        public HashSet<string> ReportedLsg005Locations { get; } = new();
    }
}
