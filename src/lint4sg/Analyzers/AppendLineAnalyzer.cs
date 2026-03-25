using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG010: AppendLine/Append with 8+ consecutive spaces or 2+ consecutive tabs.
/// LSG011: 3+ consecutive AppendLine calls without branching (use raw string literal).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AppendLineAnalyzer : DiagnosticAnalyzer
{
    private const int MinConsecutiveSpaces = 8;
    private const int MinConsecutiveTabs = 2;
    private const int MinConsecutiveAppendLines = 3;
    private const string AnalysisClassName = "__Lint4sgAnalysisContext";
    private const string AnalysisMethodName = "__GeneratedCodeFragment";

    // Matches 8 or more consecutive spaces
    private static readonly Regex ExcessiveSpacesPattern = new(@" {8,}", RegexOptions.Compiled);

    // Matches 2 or more consecutive tabs
    private static readonly Regex ExcessiveTabsPattern = new(@"\t{2,}", RegexOptions.Compiled);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.LSG010,
            DiagnosticDescriptors.LSG011,
            DiagnosticDescriptors.LSG015,
            DiagnosticDescriptors.LSG021
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBlock, SyntaxKind.Block);
        context.RegisterSyntaxNodeAction(
            AnalyzeInvocationForWhitespace,
            SyntaxKind.InvocationExpression
        );
    }

    private static void AnalyzeInvocationForWhitespace(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (methodName != "AppendLine" && methodName != "Append")
            return;

        // Check string argument for excessive whitespace
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 1)
            return;

        var arg = args[0].Expression;

        // Get the string value
        var stringValue = GetStringValue(arg, context.SemanticModel, invocation.SpanStart);
        if (stringValue == null)
            return;

        if (IsRawStringLiteral(arg))
        {
            if (IsFullyIndentedRawString(stringValue))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.LSG015, invocation.GetLocation())
                );
            }
        }
        else if (
            ExcessiveSpacesPattern.IsMatch(stringValue) || ExcessiveTabsPattern.IsMatch(stringValue)
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(DiagnosticDescriptors.LSG010, invocation.GetLocation())
            );
        }

        if (ContainsNonFullyQualifiedTypeUsage(stringValue))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(DiagnosticDescriptors.LSG021, invocation.GetLocation())
            );
        }
    }

    private static void AnalyzeBlock(SyntaxNodeAnalysisContext context)
    {
        var block = (BlockSyntax)context.Node;

        // Find consecutive AppendLine/Append statements
        AnalyzeConsecutiveAppendLines(context, block.Statements);
    }

    private static void AnalyzeConsecutiveAppendLines(
        SyntaxNodeAnalysisContext context,
        SyntaxList<StatementSyntax> statements
    )
    {
        int consecutiveCount = 0;
        StatementSyntax? firstStatement = null;

        foreach (var statement in statements)
        {
            if (IsAppendLineStatement(statement))
            {
                consecutiveCount++;
                if (firstStatement == null)
                    firstStatement = statement;
            }
            else
            {
                if (consecutiveCount >= MinConsecutiveAppendLines && firstStatement != null)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            DiagnosticDescriptors.LSG011,
                            firstStatement.GetLocation(),
                            consecutiveCount
                        )
                    );
                }
                consecutiveCount = 0;
                firstStatement = null;
            }
        }

        // Check at end
        if (consecutiveCount >= MinConsecutiveAppendLines && firstStatement != null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.LSG011,
                    firstStatement.GetLocation(),
                    consecutiveCount
                )
            );
        }
    }

    private static bool IsAppendLineStatement(StatementSyntax statement)
    {
        // Match: expr.AppendLine(...) or chained: sb.AppendLine(...).AppendLine(...)
        // We look for an ExpressionStatement containing an invocation with AppendLine

        if (statement is not ExpressionStatementSyntax exprStatement)
            return false;

        return ContainsAppendLineCall(exprStatement.Expression);
    }

    private static bool ContainsAppendLineCall(ExpressionSyntax expression)
    {
        // Handle chained calls: sb.AppendLine(...).AppendLine(...)
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodName = memberAccess.Name.Identifier.Text;
                if (methodName == "AppendLine" || methodName == "Append")
                    return true;

                // Check the receiver for chaining
                return ContainsAppendLineCall(memberAccess.Expression);
            }
        }

        // Handle assignment: sb = sb.AppendLine(...)
        if (expression is AssignmentExpressionSyntax assignment)
        {
            return ContainsAppendLineCall(assignment.Right);
        }

        return false;
    }

    private static bool IsRawStringLiteral(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax literal)
        {
            var token = literal.Token;
            return token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken)
                || token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken)
                || token.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken)
                || token.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);
        }

        // Also check for interpolated raw strings
        if (expr is InterpolatedStringExpressionSyntax interpolated)
        {
            var startToken = interpolated.StringStartToken;
            return startToken.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken)
                || startToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);
        }

        return false;
    }

    private static string? GetStringValue(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        int currentPosition
    ) => GetStringValue(expr, semanticModel, currentPosition, visitedLocals: null);

    private static string? GetStringValue(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        int currentPosition,
        HashSet<ILocalSymbol>? visitedLocals
    )
    {
        if (
            expr is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression)
        )
        {
            return literal.Token.ValueText;
        }

        if (expr is InterpolatedStringExpressionSyntax interpolated)
        {
            return GetInterpolatedStringValue(
                interpolated,
                semanticModel,
                currentPosition,
                visitedLocals
            );
        }

        var constantValue = semanticModel.GetConstantValue(expr);
        if (constantValue is { HasValue: true, Value: string constantString })
        {
            return constantString;
        }

        if (expr is ParenthesizedExpressionSyntax parenthesized)
        {
            return GetStringValue(
                parenthesized.Expression,
                semanticModel,
                currentPosition,
                visitedLocals
            );
        }

        if (expr is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } addExpression)
        {
            var left = GetStringValue(
                addExpression.Left,
                semanticModel,
                currentPosition,
                visitedLocals
            );
            var right = GetStringValue(
                addExpression.Right,
                semanticModel,
                currentPosition,
                visitedLocals
            );
            return left != null && right != null ? left + right : null;
        }

        if (expr is IdentifierNameSyntax identifier)
        {
            return ResolveLocalStringValue(
                identifier,
                semanticModel,
                currentPosition,
                visitedLocals
            );
        }

        return null;
    }

    private static bool IsFullyIndentedRawString(string value)
    {
        var lines = value
            .Split('\n')
            .Select(static line => line.TrimEnd('\r'))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return lines.Length > 1 && lines.All(static line => char.IsWhiteSpace(line[0]));
    }

    private static bool ContainsNonFullyQualifiedTypeUsage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed =
            char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]) ? value.Trim() : value;

        if (!MightContainTypeUsage(trimmed))
            return false;

        return GetStatementCandidates(trimmed).Any(ContainsNonFullyQualifiedTypeUsageInStatement)
            || GetMemberCandidates(trimmed).Any(ContainsNonFullyQualifiedTypeUsageInMember);
    }

    private static bool MightContainTypeUsage(string value)
    {
        if (!ContainsLetter(value))
            return false;

        return value.IndexOf('<') >= 0
            || value.IndexOf(':') >= 0
            || value.IndexOf('(') >= 0
            || value.IndexOf('=') >= 0
            || value.IndexOf(';') >= 0
            || value.Contains(" new ", StringComparison.Ordinal)
            || value.StartsWith("new ", StringComparison.Ordinal)
            || value.Contains("typeof", StringComparison.Ordinal)
            || value.Contains("default", StringComparison.Ordinal)
            || value.Contains("class ", StringComparison.Ordinal)
            || value.Contains("struct ", StringComparison.Ordinal)
            || value.Contains("interface ", StringComparison.Ordinal)
            || value.Contains("record ", StringComparison.Ordinal)
            || value.Contains("public ", StringComparison.Ordinal)
            || value.Contains("private ", StringComparison.Ordinal)
            || value.Contains("internal ", StringComparison.Ordinal)
            || value.Contains("protected ", StringComparison.Ordinal);
    }

    private static bool ContainsLetter(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsLetter(ch))
                return true;
        }

        return false;
    }

    private static string? GetInterpolatedStringValue(
        InterpolatedStringExpressionSyntax interpolated,
        SemanticModel semanticModel,
        int currentPosition,
        HashSet<ILocalSymbol>? visitedLocals
    )
    {
        // Reconstruct interpolated strings on a best-effort basis so analyzable
        // embedded string fragments can still participate in downstream checks.
        var parts = new StringBuilder();

        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    parts.Append(text.TextToken.ValueText);
                    break;
                case InterpolationSyntax interpolation:
                {
                    var value = GetStringValue(
                        interpolation.Expression,
                        semanticModel,
                        currentPosition,
                        visitedLocals
                    );
                    if (value != null)
                    {
                        parts.Append(value);
                    }

                    break;
                }
            }
        }

        return parts.ToString();
    }

    private static IEnumerable<string> GetStatementCandidates(string text)
    {
        yield return text;

        if (!EndsWithStatementTerminator(text))
        {
            yield return text + ";";
        }
    }

    private static IEnumerable<string> GetMemberCandidates(string text)
    {
        yield return text;

        var looksLikeIncompleteMethodSignature = LooksLikeIncompleteMethodSignature(text);
        if (looksLikeIncompleteMethodSignature)
        {
            yield return text + " { }";
        }

        if (!EndsWithStatementTerminator(text) && !looksLikeIncompleteMethodSignature)
        {
            yield return text + ";";
        }
    }

    private static bool EndsWithStatementTerminator(string text) =>
        text.EndsWith(";", StringComparison.Ordinal) || text.EndsWith("}", StringComparison.Ordinal);

    private static bool LooksLikeIncompleteMethodSignature(string text) =>
        !EndsWithStatementTerminator(text)
        && !text.Contains('{')
        && !text.Contains("=>", StringComparison.Ordinal)
        && text.Contains('(')
        && text.Contains(')');

    private static bool ContainsNonFullyQualifiedTypeUsageInStatement(string statementText)
    {
        var compilationUnit = SyntaxFactory.ParseCompilationUnit(
            $"class {AnalysisClassName} {{ void {AnalysisMethodName}() {{ {statementText} }} }}"
        );

        if (HasParserErrors(compilationUnit))
            return false;

        var body = compilationUnit
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(static method => method.Identifier.ValueText == AnalysisMethodName)
            ?.Body;

        return body != null && ContainsNonFullyQualifiedTypeSyntax(body);
    }

    private static bool ContainsNonFullyQualifiedTypeUsageInMember(string memberText)
    {
        var compilationUnit = SyntaxFactory.ParseCompilationUnit(
            $"class {AnalysisClassName} {{ {memberText} }}"
        );

        if (HasParserErrors(compilationUnit))
            return false;

        var member = compilationUnit
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(static classDeclaration =>
                classDeclaration.Identifier.ValueText == AnalysisClassName
            )
            ?.Members.FirstOrDefault();

        if (member is null)
            return false;

        var allowedTypeParameterNames = member
            .DescendantNodes()
            .OfType<TypeParameterSyntax>()
            .Select(static typeParameter => typeParameter.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        return ContainsNonFullyQualifiedTypeSyntax(member, allowedTypeParameterNames);
    }

    private static bool HasParserErrors(SyntaxNode node) =>
        node.GetDiagnostics().Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static bool ContainsNonFullyQualifiedTypeSyntax(SyntaxNode node) =>
        ContainsNonFullyQualifiedTypeSyntax(node, allowedTypeParameterNames: null);

    private static bool ContainsNonFullyQualifiedTypeSyntax(
        SyntaxNode node,
        ISet<string>? allowedTypeParameterNames
    ) =>
        node.DescendantNodes()
            .OfType<TypeSyntax>()
            .Where(static typeSyntax => typeSyntax.Parent is not TypeSyntax)
            .Any(typeSyntax => !IsAllowedTypeSyntax(typeSyntax, allowedTypeParameterNames));

    private static bool IsAllowedTypeSyntax(TypeSyntax typeSyntax) =>
        IsAllowedTypeSyntax(typeSyntax, allowedTypeParameterNames: null);

    private static bool IsAllowedTypeSyntax(
        TypeSyntax typeSyntax,
        ISet<string>? allowedTypeParameterNames
    ) =>
        typeSyntax switch
        {
            PredefinedTypeSyntax => true,
            IdentifierNameSyntax identifierName => IsAllowedIdentifierTypeSyntax(
                identifierName,
                allowedTypeParameterNames
            ),
            NullableTypeSyntax nullableType => IsAllowedTypeSyntax(
                nullableType.ElementType,
                allowedTypeParameterNames
            ),
            ArrayTypeSyntax arrayType => IsAllowedTypeSyntax(
                arrayType.ElementType,
                allowedTypeParameterNames
            ),
            PointerTypeSyntax pointerType => IsAllowedTypeSyntax(
                pointerType.ElementType,
                allowedTypeParameterNames
            ),
            RefTypeSyntax refType => IsAllowedTypeSyntax(refType.Type, allowedTypeParameterNames),
            TupleTypeSyntax tupleType => tupleType.Elements.All(element =>
                IsAllowedTypeSyntax(element.Type, allowedTypeParameterNames)
            ),
            QualifiedNameSyntax qualifiedName => HasGlobalAliasRoot(qualifiedName)
                && IsAllowedQualifiedTypeName(qualifiedName, allowedTypeParameterNames),
            AliasQualifiedNameSyntax aliasQualifiedName => aliasQualifiedName.Alias.Identifier.ValueText
                    == "global"
                && IsAllowedQualifiedTypeName(aliasQualifiedName.Name, allowedTypeParameterNames),
            _ => false,
        };

    private static bool IsAllowedQualifiedTypeName(NameSyntax nameSyntax) =>
        IsAllowedQualifiedTypeName(nameSyntax, allowedTypeParameterNames: null);

    private static bool IsAllowedQualifiedTypeName(
        NameSyntax nameSyntax,
        ISet<string>? allowedTypeParameterNames
    ) =>
        nameSyntax switch
        {
            IdentifierNameSyntax => true,
            GenericNameSyntax genericName => genericName.TypeArgumentList.Arguments.All(typeArgument =>
                IsAllowedTypeSyntax(typeArgument, allowedTypeParameterNames)
            ),
            AliasQualifiedNameSyntax aliasQualifiedName => aliasQualifiedName.Alias.Identifier.ValueText
                    == "global"
                && IsAllowedQualifiedTypeName(aliasQualifiedName.Name, allowedTypeParameterNames),
            QualifiedNameSyntax qualifiedName => IsAllowedQualifiedTypeName(
                    qualifiedName.Left,
                    allowedTypeParameterNames
                )
                && IsAllowedQualifiedTypeName(qualifiedName.Right, allowedTypeParameterNames),
            _ => false,
        };

    private static bool IsAllowedIdentifierTypeSyntax(
        IdentifierNameSyntax identifierName,
        ISet<string>? allowedTypeParameterNames
    )
    {
        if (identifierName.IsVar)
            return true;

        var identifierText = identifierName.Identifier.ValueText;
        if (string.Equals(identifierText, "dynamic", StringComparison.Ordinal))
            return true;

        return allowedTypeParameterNames is not null
            && allowedTypeParameterNames.Contains(identifierText);
    }

    private static bool HasGlobalAliasRoot(NameSyntax nameSyntax) =>
        nameSyntax switch
        {
            AliasQualifiedNameSyntax aliasQualifiedName =>
                aliasQualifiedName.Alias.Identifier.ValueText == "global",
            QualifiedNameSyntax qualifiedName => HasGlobalAliasRoot(qualifiedName.Left),
            _ => false,
        };

    private static string? ResolveLocalStringValue(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        int currentPosition,
        HashSet<ILocalSymbol>? visitedLocals
    )
    {
        if (GetReferencedSymbol(semanticModel, identifier) is not ILocalSymbol localSymbol)
            return null;

        visitedLocals ??= new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        if (!visitedLocals.Add(localSymbol))
            return null;

        try
        {
            var assignedExpression = FindAssignedExpression(
                identifier,
                localSymbol,
                semanticModel,
                currentPosition
            );
            return assignedExpression == null
                ? null
                : GetStringValue(assignedExpression, semanticModel, currentPosition, visitedLocals);
        }
        finally
        {
            visitedLocals.Remove(localSymbol);
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
            if (assignment.SpanStart >= currentPosition)
            {
                break;
            }

            if (GetReferencedSymbol(semanticModel, assignment.Left) is not ILocalSymbol assignedLocal)
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(assignedLocal, localSymbol))
            {
                continue;
            }

            latestExpression = assignment.Right;
        }

        return latestExpression;
    }

    private static ISymbol? GetReferencedSymbol(SemanticModel semanticModel, SyntaxNode node)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(node);
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }
}
