using System.Collections.Immutable;
using System.Linq;
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

    // Matches 8 or more consecutive spaces
    private static readonly Regex ExcessiveSpacesPattern = new(@" {8,}", RegexOptions.Compiled);
    // Matches 2 or more consecutive tabs
    private static readonly Regex ExcessiveTabsPattern = new(@"\t{2,}", RegexOptions.Compiled);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.LSG010,
            DiagnosticDescriptors.LSG011,
            DiagnosticDescriptors.LSG015);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBlock, SyntaxKind.Block);
        context.RegisterSyntaxNodeAction(AnalyzeInvocationForWhitespace, SyntaxKind.InvocationExpression);
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
        var stringValue = GetStringValue(arg);
        if (stringValue == null)
            return;

        if (IsRawStringLiteral(arg))
        {
            if (IsFullyIndentedRawString(stringValue))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG015,
                    invocation.GetLocation()));
            }
            return;
        }

        if (ExcessiveSpacesPattern.IsMatch(stringValue) || ExcessiveTabsPattern.IsMatch(stringValue))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.LSG010,
                invocation.GetLocation()));
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
        SyntaxList<StatementSyntax> statements)
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
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.LSG011,
                        firstStatement.GetLocation(),
                        consecutiveCount));
                }
                consecutiveCount = 0;
                firstStatement = null;
            }
        }

        // Check at end
        if (consecutiveCount >= MinConsecutiveAppendLines && firstStatement != null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.LSG011,
                firstStatement.GetLocation(),
                consecutiveCount));
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
            return token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken) ||
                   token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) ||
                   token.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken) ||
                   token.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);
        }

        // Also check for interpolated raw strings
        if (expr is InterpolatedStringExpressionSyntax interpolated)
        {
            var startToken = interpolated.StringStartToken;
            return startToken.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken) ||
                   startToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);
        }

        return false;
    }

    private static string? GetStringValue(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        if (expr is InterpolatedStringExpressionSyntax interpolated)
        {
            // For interpolated strings, reconstruct the static parts to check for whitespace
            var parts = new System.Text.StringBuilder();
            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                    parts.Append(text.TextToken.ValueText);
            }
            return parts.ToString();
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

        return lines.Length > 0 &&
               lines.All(static line => char.IsWhiteSpace(line[0]));
    }
}
