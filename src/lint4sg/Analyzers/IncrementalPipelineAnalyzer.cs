using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG017: Pipeline callbacks should be static when possible.
/// LSG018: Prefer SelectMany over carrying materialized collections through the pipeline.
/// LSG019: Delay Collect until whole-set aggregation is actually needed.
/// LSG020: Avoid nested tuple proliferation in pipeline composition.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IncrementalPipelineAnalyzer : DiagnosticAnalyzer
{
    private const string GenericTupleGuidance =
        "Flatten the model or introduce a named type.";
    private const string SameTypeTupleMergeGuidance =
        "Because matching Left and Right branches have the same type, merge them first with a helper such as MergeCollectedValues<T>(first, second).";

    private static readonly ImmutableHashSet<string> PipelineCallbackMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Select",
            "SelectMany",
            "Where",
            "CreateSyntaxProvider",
            "ForAttributeWithMetadataName",
            "RegisterSourceOutput",
            "RegisterImplementationSourceOutput"
        );

    private static readonly ImmutableHashSet<string> CollectionConsumerMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Select",
            "SelectMany",
            "Where",
            "RegisterSourceOutput",
            "RegisterImplementationSourceOutput"
        );

    private static readonly ImmutableHashSet<string> ElementwiseMethodNames =
        ImmutableHashSet.Create(StringComparer.Ordinal, "Select", "SelectMany", "Where");

    private readonly struct TupleProliferationDiagnostic
    {
        public TupleProliferationDiagnostic(Location location, string guidance)
        {
            Location = location;
            Guidance = guidance;
        }

        public Location Location { get; }

        public string Guidance { get; }
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.LSG017,
            DiagnosticDescriptors.LSG018,
            DiagnosticDescriptors.LSG019,
            DiagnosticDescriptors.LSG020
        );

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

        var methodName = memberAccess.Name.Identifier.Text;

        if (
            PipelineCallbackMethods.Contains(methodName)
            && IsTrackedPipelineMethod(context.SemanticModel, methodName, memberAccess.Expression)
        )
        {
            AnalyzeCallbacks(context, invocation, methodName);
        }

        if (
            CollectionConsumerMethods.Contains(methodName)
            && IsTrackedCollectionConsumer(context.SemanticModel, methodName, memberAccess.Expression)
        )
        {
            AnalyzeCollectionFlow(context, invocation, methodName, memberAccess.Expression);
        }
    }

    private static void AnalyzeCallbacks(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string methodName
    )
    {
        var callbackSourceExpression = GetCallbackSourceExpression(invocation, methodName);

        foreach (var callback in GetCallbackExpressions(invocation, methodName))
        {
            if (!HasStaticModifier(callback) && CanBeStatic(callback, context.SemanticModel))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.LSG017, callback.GetLocation())
                );
            }

            if (
                callbackSourceExpression != null
                && TryFindTupleProliferation(
                    callback,
                    context.SemanticModel,
                    callbackSourceExpression,
                    invocation.SpanStart
                ) is { } tupleDiagnostic
            )
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.LSG020,
                        tupleDiagnostic.Location,
                        tupleDiagnostic.Guidance
                    )
                );
            }
        }

        // Also check method-group callbacks for LSG017.
        foreach (var expression in GetCallbackArgumentExpressions(invocation, methodName))
        {
            if (expression is AnonymousFunctionExpressionSyntax)
                continue;

            if (
                GetReferencedSymbol(context.SemanticModel, expression)
                is IMethodSymbol { IsStatic: false }
            )
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.LSG017, expression.GetLocation())
                );
            }
        }
    }

    private static ExpressionSyntax? GetCallbackSourceExpression(
        InvocationExpressionSyntax invocation,
        string methodName
    ) =>
        methodName is "RegisterSourceOutput" or "RegisterImplementationSourceOutput"
            ? invocation.ArgumentList.Arguments.Count > 0
                ? invocation.ArgumentList.Arguments[0].Expression
                : null
            : invocation.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Expression
                : null;

    private static void AnalyzeCollectionFlow(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string methodName,
        ExpressionSyntax receiverExpression
    )
    {
        var sourceExpression =
            methodName is "RegisterSourceOutput" or "RegisterImplementationSourceOutput"
            && invocation.ArgumentList.Arguments.Count > 0
                ? invocation.ArgumentList.Arguments[0].Expression
                : receiverExpression;

        var producerInvocation = ResolveProducerInvocation(
            sourceExpression,
            context.SemanticModel,
            invocation.SpanStart,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default)
        );
        if (producerInvocation == null)
            return;

        var providerTypeInfo = context.SemanticModel.GetTypeInfo(sourceExpression);
        var providerElementType = GetProviderElementType(
            providerTypeInfo.Type ?? providerTypeInfo.ConvertedType
        );
        if (providerElementType == null || !IsMaterializedCollectionType(providerElementType))
            return;

        var callback = GetPrimaryCallback(invocation, methodName);
        if (callback == null)
            return;

        var collectionParameter = GetCollectionParameterSymbol(
            callback,
            methodName,
            context.SemanticModel
        );
        if (collectionParameter == null)
            return;

        if (
            !ConsumesCollectionElementwise(
                callback,
                collectionParameter,
                context.SemanticModel,
                methodName
            )
        )
        {
            return;
        }

        var producerMethodName = GetInvokedMethodName(producerInvocation);
        var descriptor =
            producerMethodName == "Collect"
                ? DiagnosticDescriptors.LSG019
                : DiagnosticDescriptors.LSG018;

        var diagnostic =
            descriptor == DiagnosticDescriptors.LSG018
                ? Diagnostic.Create(
                    descriptor,
                    producerInvocation.GetLocation(),
                    providerElementType.ToDisplayString()
                )
                : Diagnostic.Create(descriptor, producerInvocation.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsTrackedPipelineMethod(
        SemanticModel semanticModel,
        string methodName,
        ExpressionSyntax receiverExpression
    )
    {
        var receiverType = semanticModel.GetTypeInfo(receiverExpression).Type;
        return methodName switch
        {
            "CreateSyntaxProvider" or "ForAttributeWithMetadataName" => IsType(
                receiverType,
                "SyntaxValueProvider",
                "Microsoft.CodeAnalysis"
            ),
            "RegisterSourceOutput" or "RegisterImplementationSourceOutput" => IsType(
                receiverType,
                "IncrementalGeneratorInitializationContext",
                "Microsoft.CodeAnalysis"
            ),
            _ => IsIncrementalProviderType(receiverType),
        };
    }

    private static bool IsTrackedCollectionConsumer(
        SemanticModel semanticModel,
        string methodName,
        ExpressionSyntax receiverExpression
    ) => IsTrackedPipelineMethod(semanticModel, methodName, receiverExpression);

    private static bool IsIncrementalProviderType(ITypeSymbol? type) =>
        type is INamedTypeSymbol namedType
        && namedType.IsGenericType
        && namedType.ContainingNamespace?.ToString() == "Microsoft.CodeAnalysis"
        && (
            namedType.Name == "IncrementalValueProvider"
            || namedType.Name == "IncrementalValuesProvider"
        );

    private static bool IsType(ITypeSymbol? type, string name, string namespaceName) =>
        type is INamedTypeSymbol namedType
        && namedType.Name == name
        && namedType.ContainingNamespace?.ToString() == namespaceName;

    private static IEnumerable<AnonymousFunctionExpressionSyntax> GetCallbackExpressions(
        InvocationExpressionSyntax invocation,
        string methodName
    )
    {
        var arguments = invocation.ArgumentList.Arguments;
        return methodName switch
        {
            "CreateSyntaxProvider" => GetAnonymousFunctions(arguments, 0, 1),
            "ForAttributeWithMetadataName" => GetAnonymousFunctions(arguments, 1, 2),
            "RegisterSourceOutput" or "RegisterImplementationSourceOutput" =>
                GetAnonymousFunctions(arguments, 1),
            _ => arguments
                .Select(argument => AsAnonymousFunction(argument.Expression))
                .OfType<AnonymousFunctionExpressionSyntax>(),
        };
    }

    private static IEnumerable<AnonymousFunctionExpressionSyntax> GetAnonymousFunctions(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        params int[] indices
    )
    {
        foreach (var index in indices)
        {
            if (index >= arguments.Count)
                continue;

            if (AsAnonymousFunction(arguments[index].Expression) is { } callback)
                yield return callback;
        }
    }

    private static IEnumerable<ExpressionSyntax> GetCallbackArgumentExpressions(
        InvocationExpressionSyntax invocation,
        string methodName
    )
    {
        var arguments = invocation.ArgumentList.Arguments;
        return methodName switch
        {
            "CreateSyntaxProvider" => GetExpressionsAtIndices(arguments, 0, 1),
            "ForAttributeWithMetadataName" => GetExpressionsAtIndices(arguments, 1, 2),
            "RegisterSourceOutput" or "RegisterImplementationSourceOutput" =>
                GetExpressionsAtIndices(arguments, 1),
            _ => arguments.Select(argument => UnwrapExpression(argument.Expression)),
        };
    }

    private static IEnumerable<ExpressionSyntax> GetExpressionsAtIndices(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        params int[] indices
    )
    {
        foreach (var index in indices)
        {
            if (index < arguments.Count)
                yield return UnwrapExpression(arguments[index].Expression);
        }
    }

    private static AnonymousFunctionExpressionSyntax? GetPrimaryCallback(
        InvocationExpressionSyntax invocation,
        string methodName
    )
    {
        var arguments = invocation.ArgumentList.Arguments;
        return methodName switch
        {
            "RegisterSourceOutput" or "RegisterImplementationSourceOutput" => arguments.Count > 1
                ? AsAnonymousFunction(arguments[1].Expression)
                : null,
            _ => arguments
                .Select(argument => AsAnonymousFunction(argument.Expression))
                .FirstOrDefault(callback => callback != null),
        };
    }

    private static AnonymousFunctionExpressionSyntax? AsAnonymousFunction(ExpressionSyntax expression) =>
        UnwrapExpression(expression) as AnonymousFunctionExpressionSyntax;

    private static bool HasStaticModifier(AnonymousFunctionExpressionSyntax callback) =>
        callback switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Modifiers.Any(SyntaxKind.StaticKeyword),
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => parenthesizedLambda
                .Modifiers.Any(SyntaxKind.StaticKeyword),
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod
                .Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false,
        };

    private static bool CanBeStatic(
        AnonymousFunctionExpressionSyntax callback,
        SemanticModel semanticModel
    )
    {
        foreach (var node in EnumerateRelevantNodes(callback))
        {
            if (node is ThisExpressionSyntax or BaseExpressionSyntax)
                return false;

            if (node is not IdentifierNameSyntax identifier)
                continue;

            var symbol = GetReferencedSymbol(semanticModel, identifier);
            if (symbol == null)
                continue;

            switch (symbol)
            {
                case ILocalSymbol localSymbol:
                    if (!localSymbol.IsConst && !IsDeclaredWithinCallback(symbol, callback))
                        return false;
                    break;
                case IParameterSymbol or IRangeVariableSymbol:
                    if (!IsDeclaredWithinCallback(symbol, callback))
                        return false;
                    break;
                case IFieldSymbol fieldSymbol when !fieldSymbol.IsStatic:
                    if (UsesImplicitInstanceReceiver(identifier))
                        return false;
                    break;
                case IPropertySymbol propertySymbol when !propertySymbol.IsStatic:
                    if (UsesImplicitInstanceReceiver(identifier))
                        return false;
                    break;
                case IEventSymbol eventSymbol when !eventSymbol.IsStatic:
                    if (UsesImplicitInstanceReceiver(identifier))
                        return false;
                    break;
                case IMethodSymbol methodSymbol
                    when methodSymbol.MethodKind != MethodKind.AnonymousFunction
                        && !methodSymbol.IsStatic
                        && UsesImplicitInstanceReceiver(identifier):
                    return false;
            }
        }

        return true;
    }

    private static IEnumerable<SyntaxNode> EnumerateRelevantNodes(
        AnonymousFunctionExpressionSyntax callback
    )
    {
        var body = callback.Body;
        return body.DescendantNodesAndSelf(node =>
            node == body || node is not AnonymousFunctionExpressionSyntax
        );
    }

    private static bool IsDeclaredWithinCallback(
        ISymbol symbol,
        AnonymousFunctionExpressionSyntax callback
    )
    {
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            if (callback.Span.Contains(syntaxReference.GetSyntax().Span))
                return true;
        }

        return false;
    }

    private static bool UsesImplicitInstanceReceiver(IdentifierNameSyntax identifier)
    {
        if (
            identifier.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name == identifier
        )
        {
            return memberAccess.Expression is ThisExpressionSyntax or BaseExpressionSyntax;
        }

        return true;
    }

    private static TupleProliferationDiagnostic? TryFindTupleProliferation(
        AnonymousFunctionExpressionSyntax callback,
        SemanticModel semanticModel,
        ExpressionSyntax sourceExpression,
        int currentPosition
    )
    {
        var body = callback.Body;
        var guidance = GetTupleProliferationGuidance(body, semanticModel);
        var nestedTuple = body
            .DescendantNodesAndSelf()
            .OfType<TupleExpressionSyntax>()
            .FirstOrDefault(tuple =>
                tuple.Arguments.Any(argument => UnwrapExpression(argument.Expression) is TupleExpressionSyntax)
            );
        if (nestedTuple != null)
            return new TupleProliferationDiagnostic(nestedTuple.GetLocation(), guidance);

        var deepNavigation = body
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(memberAccess =>
                GetTupleNavigationDepth(memberAccess, semanticModel, callback) >= 2
            );
        if (deepNavigation != null)
            return new TupleProliferationDiagnostic(deepNavigation.GetLocation(), guidance);

        return TryFindNestedTupleParameter(
            callback,
            semanticModel,
            sourceExpression,
            currentPosition
        );
    }

    private static TupleProliferationDiagnostic? TryFindNestedTupleParameter(
        AnonymousFunctionExpressionSyntax callback,
        SemanticModel semanticModel,
        ExpressionSyntax sourceExpression,
        int currentPosition
    )
    {
        if (
            GetCombineChainDepth(
                sourceExpression,
                semanticModel,
                currentPosition,
                new HashSet<ISymbol>(SymbolEqualityComparer.Default)
            ) < 2
        )
        {
            return null;
        }

        foreach (var parameterSyntax in GetParameterSyntaxes(callback))
        {
            if (
                semanticModel.GetDeclaredSymbol(parameterSyntax) is IParameterSymbol { Type: { } type }
                && ContainsTupleWithinTuple(type)
            )
            {
                return new TupleProliferationDiagnostic(
                    parameterSyntax.GetLocation(),
                    GetTupleProliferationGuidance(type)
                );
            }
        }

        return null;
    }

    private static string GetTupleProliferationGuidance(
        CSharpSyntaxNode node,
        SemanticModel semanticModel
    ) => HasSameTypeLeftRightBranches(node, semanticModel)
        ? SameTypeTupleMergeGuidance
        : GenericTupleGuidance;

    private static string GetTupleProliferationGuidance(ITypeSymbol type) =>
        HasSameTypeLeftRightBranches(type) ? SameTypeTupleMergeGuidance : GenericTupleGuidance;

    private static bool HasSameTypeLeftRightBranches(
        CSharpSyntaxNode node,
        SemanticModel semanticModel
    )
    {
        var accesses = node
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(memberAccess => memberAccess.Name.Identifier.Text is "Left" or "Right")
            .Select(memberAccess => new
            {
                Side = memberAccess.Name.Identifier.Text,
                BaseExpression = UnwrapExpression(memberAccess.Expression),
                Type = semanticModel.GetTypeInfo(memberAccess).Type,
            })
            .Where(access => access.Type != null)
            .ToArray();

        foreach (var leftAccess in accesses.Where(access => access.Side == "Left"))
        {
            foreach (var rightAccess in accesses.Where(access => access.Side == "Right"))
            {
                if (
                    leftAccess.BaseExpression.IsEquivalentTo(rightAccess.BaseExpression)
                    && SymbolEqualityComparer.Default.Equals(leftAccess.Type, rightAccess.Type)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSameTypeLeftRightBranches(ITypeSymbol type)
    {
        var typeStack = new Stack<ITypeSymbol>();
        typeStack.Push(type);

        while (typeStack.Count > 0)
        {
            if (typeStack.Pop() is not INamedTypeSymbol { IsTupleType: true } tupleType)
                continue;

            var tupleElements = tupleType.TupleElements;
            if (
                tupleElements.Length >= 2
                && SymbolEqualityComparer.Default.Equals(
                    tupleElements[0].Type,
                    tupleElements[1].Type
                )
            )
            {
                return true;
            }

            foreach (var tupleElement in tupleElements)
            {
                typeStack.Push(tupleElement.Type);
            }
        }

        return false;
    }

    private static bool ContainsTupleWithinTuple(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsTupleType: true } rootTupleType)
            return false;

        var typeStack = new Stack<ITypeSymbol>();
        foreach (var tupleElement in rootTupleType.TupleElements)
        {
            typeStack.Push(tupleElement.Type);
        }

        while (typeStack.Count > 0)
        {
            if (typeStack.Pop() is not INamedTypeSymbol { IsTupleType: true } tupleType)
                continue;

            return true;
        }

        return false;
    }

    private static int GetCombineChainDepth(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        int currentPosition,
        HashSet<ISymbol> visitedSymbols
    )
    {
        expression = UnwrapExpression(expression);

        switch (expression)
        {
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                if (memberAccess.Name.Identifier.Text == "Combine")
                {
                    var receiverDepth = GetCombineChainDepth(
                        memberAccess.Expression,
                        semanticModel,
                        currentPosition,
                        visitedSymbols
                    );
                    var argumentDepth =
                        invocation.ArgumentList.Arguments.Count > 0
                            ? GetCombineChainDepth(
                                invocation.ArgumentList.Arguments[0].Expression,
                                semanticModel,
                                currentPosition,
                                visitedSymbols
                            )
                            : 0;
                    return 1 + (receiverDepth > argumentDepth ? receiverDepth : argumentDepth);
                }

                return memberAccess.Name.Identifier.Text == "Where"
                    ? GetCombineChainDepth(
                        memberAccess.Expression,
                        semanticModel,
                        currentPosition,
                        visitedSymbols
                    )
                    : 0;
            case IdentifierNameSyntax identifier
                when GetReferencedSymbol(semanticModel, identifier) is ILocalSymbol localSymbol:
                if (!visitedSymbols.Add(localSymbol))
                    return 0;

                var assignedExpression = FindAssignedExpression(
                    identifier,
                    localSymbol,
                    semanticModel,
                    currentPosition
                );
                return assignedExpression == null
                    ? 0
                    : GetCombineChainDepth(
                        assignedExpression,
                        semanticModel,
                        currentPosition,
                        visitedSymbols
                    );
            default:
                return 0;
        }
    }

    private static int GetTupleNavigationDepth(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        AnonymousFunctionExpressionSyntax callback
    )
    {
        var current = (ExpressionSyntax)memberAccess;
        var depth = 0;

        while (
            current is MemberAccessExpressionSyntax currentMemberAccess
            && currentMemberAccess.Name.Identifier.Text is "Left" or "Right"
            && IsTupleElementAccess(currentMemberAccess, semanticModel)
        )
        {
            depth++;
            current = UnwrapExpression(currentMemberAccess.Expression);
        }

        if (
            depth >= 2
            && current is IdentifierNameSyntax identifier
            && GetReferencedSymbol(semanticModel, identifier) is IParameterSymbol parameter
            && IsDeclaredWithinCallback(parameter, callback)
        )
        {
            return depth;
        }

        return 0;
    }

    private static bool IsTupleElementAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel
    )
    {
        var symbol = GetReferencedSymbol(semanticModel, memberAccess.Name);
        return symbol is IFieldSymbol fieldSymbol && fieldSymbol.ContainingType.IsTupleType;
    }

    private static IParameterSymbol? GetCollectionParameterSymbol(
        AnonymousFunctionExpressionSyntax callback,
        string methodName,
        SemanticModel semanticModel
    )
    {
        var parameterSyntax =
            methodName is "RegisterSourceOutput" or "RegisterImplementationSourceOutput"
                ? GetParameterSyntax(callback, 1)
                : GetParameterSyntax(callback, 0);
        return parameterSyntax == null
            ? null
            : semanticModel.GetDeclaredSymbol(parameterSyntax) as IParameterSymbol;
    }

    private static ParameterSyntax? GetParameterSyntax(
        AnonymousFunctionExpressionSyntax callback,
        int index
    ) =>
        callback switch
        {
            SimpleLambdaExpressionSyntax simpleLambda when index == 0 => simpleLambda.Parameter,
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda
                when index < parenthesizedLambda.ParameterList.Parameters.Count =>
                parenthesizedLambda.ParameterList.Parameters[index],
            AnonymousMethodExpressionSyntax anonymousMethod
                when anonymousMethod.ParameterList != null
                    && index < anonymousMethod.ParameterList.Parameters.Count =>
                anonymousMethod.ParameterList.Parameters[index],
            _ => null,
        };

    private static IEnumerable<ParameterSyntax> GetParameterSyntaxes(
        AnonymousFunctionExpressionSyntax callback
    ) =>
        callback switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => [simpleLambda.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => parenthesizedLambda
                .ParameterList
                .Parameters,
            AnonymousMethodExpressionSyntax anonymousMethod
                when anonymousMethod.ParameterList != null => anonymousMethod.ParameterList.Parameters,
            _ => [],
        };

    private static bool ConsumesCollectionElementwise(
        AnonymousFunctionExpressionSyntax callback,
        IParameterSymbol collectionParameter,
        SemanticModel semanticModel,
        string methodName
    )
    {
        if (
            methodName == "SelectMany"
            && TryGetReturnedExpression(callback) is { } returnedExpression
            && IsParameterReference(returnedExpression, collectionParameter, semanticModel)
        )
        {
            return true;
        }

        var body = callback.Body;

        foreach (var foreachStatement in body.DescendantNodesAndSelf().OfType<ForEachStatementSyntax>())
        {
            if (
                IsRootedInParameter(foreachStatement.Expression, collectionParameter, semanticModel)
                && !IsAggregationForeach(foreachStatement)
            )
                return true;
        }

        foreach (var nestedInvocation in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (
                nestedInvocation.Expression is MemberAccessExpressionSyntax nestedMemberAccess
                && ElementwiseMethodNames.Contains(nestedMemberAccess.Name.Identifier.Text)
                && IsRootedInParameter(
                    nestedMemberAccess.Expression,
                    collectionParameter,
                    semanticModel
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when a foreach loop body consists only of aggregation/reduction
    /// operations (e.g. accumulating into a local variable) rather than per-item
    /// projection, filtering, or side-effectful work.
    /// </summary>
    private static bool IsAggregationForeach(ForEachStatementSyntax foreachStatement)
    {
        // A foreach whose body contains expression-statement invocations (void method
        // calls such as list.Add(item) or context.AddSource(...)) or yield statements
        // is doing per-item work, not a pure aggregation.
        foreach (var node in foreachStatement.Statement.DescendantNodesAndSelf())
        {
            if (node is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax })
                return false;
            if (node is YieldStatementSyntax)
                return false;
        }

        return true;
    }

    private static bool IsParameterReference(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        SemanticModel semanticModel
    )
    {
        expression = UnwrapExpression(expression);
        return expression is IdentifierNameSyntax identifier
            && SymbolEqualityComparer.Default.Equals(
                GetReferencedSymbol(semanticModel, identifier),
                parameter
            );
    }

    private static bool IsRootedInParameter(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        SemanticModel semanticModel
    )
    {
        expression = UnwrapExpression(expression);

        return expression switch
        {
            IdentifierNameSyntax identifier => SymbolEqualityComparer.Default.Equals(
                GetReferencedSymbol(semanticModel, identifier),
                parameter
            ),
            MemberAccessExpressionSyntax memberAccess => IsRootedInParameter(
                memberAccess.Expression,
                parameter,
                semanticModel
            ),
            InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess =>
                IsRootedInParameter(memberAccess.Expression, parameter, semanticModel),
            ElementAccessExpressionSyntax elementAccess => IsRootedInParameter(
                elementAccess.Expression,
                parameter,
                semanticModel
            ),
            _ => false,
        };
    }

    private static ExpressionSyntax? TryGetReturnedExpression(
        AnonymousFunctionExpressionSyntax callback
    ) =>
        callback.Body switch
        {
            ExpressionSyntax expression => expression,
            BlockSyntax block
                when block.Statements.Count == 1
                    && block.Statements[0] is ReturnStatementSyntax
                    {
                        Expression: { } returnExpression
                    } => returnExpression,
            _ => null,
        };

    private static ITypeSymbol? GetProviderElementType(ITypeSymbol? providerType) =>
        providerType is INamedTypeSymbol namedType
        && namedType.IsGenericType
        && namedType.ContainingNamespace?.ToString() == "Microsoft.CodeAnalysis"
        && (
            namedType.Name == "IncrementalValueProvider"
            || namedType.Name == "IncrementalValuesProvider"
        )
            ? namedType.TypeArguments[0]
            : null;

    private static bool IsMaterializedCollectionType(ITypeSymbol type) =>
        type is IArrayTypeSymbol
        || (
            type is INamedTypeSymbol namedType
            && (
                (
                    namedType.Name == "List"
                    && namedType.ContainingNamespace?.ToString() == "System.Collections.Generic"
                )
                || (
                    namedType.Name == "ImmutableArray"
                    && namedType.ContainingNamespace?.ToString() == "System.Collections.Immutable"
                )
            )
        );

    private static InvocationExpressionSyntax? ResolveProducerInvocation(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        int currentPosition,
        HashSet<ISymbol> visitedSymbols
    )
    {
        expression = UnwrapExpression(expression);

        switch (expression)
        {
            case InvocationExpressionSyntax invocation:
                // If the receiver of this invocation also yields a materialized-collection
                // provider element type, this is an intermediate pass-through stage (e.g.
                // Where/Select chained after Collect). Walk back to the actual producer.
                if (invocation.Expression is MemberAccessExpressionSyntax chainedMemberAccess)
                {
                    var receiverType = semanticModel
                        .GetTypeInfo(chainedMemberAccess.Expression)
                        .Type;
                    var receiverElementType = GetProviderElementType(receiverType);
                    if (
                        receiverElementType != null
                        && IsMaterializedCollectionType(receiverElementType)
                    )
                    {
                        var innerProducer = ResolveProducerInvocation(
                            chainedMemberAccess.Expression,
                            semanticModel,
                            currentPosition,
                            visitedSymbols
                        );
                        if (innerProducer != null)
                            return innerProducer;
                    }
                }

                return invocation;
            case IdentifierNameSyntax identifier
                when GetReferencedSymbol(semanticModel, identifier) is ILocalSymbol localSymbol:
                if (!visitedSymbols.Add(localSymbol))
                    return null;

                var assignedExpression = FindAssignedExpression(
                    identifier,
                    localSymbol,
                    semanticModel,
                    currentPosition
                );
                return assignedExpression == null
                    ? null
                    : ResolveProducerInvocation(
                        assignedExpression,
                        semanticModel,
                        currentPosition,
                        visitedSymbols
                    );
            default:
                return null;
        }
    }

    private static ExpressionSyntax? FindAssignedExpression(
        IdentifierNameSyntax identifier,
        ILocalSymbol localSymbol,
        SemanticModel semanticModel,
        int currentPosition
    )
    {
        ExpressionSyntax? latestExpression = null;

        foreach (var syntaxReference in localSymbol.DeclaringSyntaxReferences)
        {
            if (
                syntaxReference.GetSyntax() is VariableDeclaratorSyntax
                {
                    Initializer: { Value: { } initializerValue }
                } declarator
                && declarator.SpanStart < currentPosition
            )
            {
                latestExpression = initializerValue;
            }
        }

        var block = identifier.AncestorsAndSelf().OfType<BlockSyntax>().FirstOrDefault();
        if (block == null)
            return latestExpression;

        foreach (var assignment in block.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (
                assignment.SpanStart >= currentPosition
                || GetReferencedSymbol(semanticModel, assignment.Left) is not ILocalSymbol assignedLocal
                || !SymbolEqualityComparer.Default.Equals(assignedLocal, localSymbol)
            )
            {
                continue;
            }

            latestExpression = assignment.Right;
        }

        return latestExpression;
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Name.Identifier.Text
            : null;

    private static ISymbol? GetReferencedSymbol(SemanticModel semanticModel, SyntaxNode node)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(node);
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }
}
