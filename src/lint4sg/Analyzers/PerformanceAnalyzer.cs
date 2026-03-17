using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG101: Consider 'in' modifier for struct parameters to avoid copying.
/// LSG102: Consider interpolated string instead of string.Format().
/// LSG103: Use StringBuilder for string concatenation in loops.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PerformanceAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.LSG101,
            DiagnosticDescriptors.LSG102,
            DiagnosticDescriptors.LSG103);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAddExpression, SyntaxKind.AddExpression);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        foreach (var parameter in method.ParameterList.Parameters)
        {
            // Skip if already has ref/in/out modifier
            if (parameter.Modifiers.Any(m =>
                m.IsKind(SyntaxKind.RefKeyword) ||
                m.IsKind(SyntaxKind.InKeyword) ||
                m.IsKind(SyntaxKind.OutKeyword)))
            {
                continue;
            }

            if (parameter.Type == null)
                continue;

            var typeSymbol = context.SemanticModel.GetSymbolInfo(parameter.Type).Symbol as ITypeSymbol;
            if (typeSymbol == null)
                continue;

            // Check if it's a struct that would benefit from 'in'
            if (typeSymbol.TypeKind == TypeKind.Struct && IsLargeStruct(typeSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG101,
                    parameter.GetLocation(),
                    parameter.Identifier.Text));
            }
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // LSG102: Detect string.Format() calls
        if (IsStringFormatCall(invocation, context.SemanticModel))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.LSG102,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeAddExpression(SyntaxNodeAnalysisContext context)
    {
        var addExpr = (BinaryExpressionSyntax)context.Node;

        // LSG103: Detect string concatenation in loops
        if (!IsInsideLoop(addExpr))
            return;

        // Check if this is a string concatenation
        var typeInfo = context.SemanticModel.GetTypeInfo(addExpr);
        if (typeInfo.Type?.SpecialType == SpecialType.System_String)
        {
            // Check if the left operand is a string variable (not just string + string literals)
            // We want to avoid flagging "abc" + "def" type of constant folding
            if (!IsConstantExpression(addExpr.Left, context.SemanticModel) ||
                !IsConstantExpression(addExpr.Right, context.SemanticModel))
            {
                // Only report on the outermost concatenation in a chain
                if (addExpr.Parent is not BinaryExpressionSyntax parentBinary ||
                    !parentBinary.IsKind(SyntaxKind.AddExpression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG103,
                        addExpr.GetLocation()));
                }
            }
        }
    }

    private static bool IsLargeStruct(ITypeSymbol type)
    {
        // Heuristic: consider a struct "large" if it has more than 2 fields
        // or if it's a known large struct type
        if (type.TypeKind != TypeKind.Struct)
            return false;

        // Skip primitive types and common small structs
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Byte:
            case SpecialType.System_Char:
            case SpecialType.System_DateTime:
            case SpecialType.System_Decimal:
            case SpecialType.System_Double:
            case SpecialType.System_Int16:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_Single:
            case SpecialType.System_UInt16:
            case SpecialType.System_UInt32:
            case SpecialType.System_UInt64:
            case SpecialType.System_SByte:
                return false;
        }

        // Count fields in the struct
        if (type is INamedTypeSymbol namedType)
        {
            var fieldCount = 0;
            foreach (var member in namedType.GetMembers())
            {
                if (member is IFieldSymbol field && !field.IsStatic)
                    fieldCount++;
            }
            return fieldCount > 2;
        }

        return false;
    }

    private static bool IsStringFormatCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol == null)
        {
            // Fallback: check by name
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                return memberAccess.Name.Identifier.Text == "Format" &&
                       memberAccess.Expression.ToString() == "string";
            return false;
        }

        return symbol.Name == "Format" &&
               symbol.ContainingType.SpecialType == SpecialType.System_String;
    }

    private static bool IsInsideLoop(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is ForStatementSyntax ||
                parent is ForEachStatementSyntax ||
                parent is WhileStatementSyntax ||
                parent is DoStatementSyntax)
            {
                return true;
            }

            // Stop at method/lambda boundaries
            if (parent is MethodDeclarationSyntax ||
                parent is LocalFunctionStatementSyntax ||
                parent is AnonymousMethodExpressionSyntax ||
                parent is SimpleLambdaExpressionSyntax ||
                parent is ParenthesizedLambdaExpressionSyntax)
            {
                return false;
            }

            parent = parent.Parent;
        }
        return false;
    }

    private static bool IsConstantExpression(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        var constantValue = semanticModel.GetConstantValue(expr);
        return constantValue.HasValue;
    }
}
