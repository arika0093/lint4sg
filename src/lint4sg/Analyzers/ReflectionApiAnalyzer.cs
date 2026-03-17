using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG013: Reflection API usage in a source generator project.
/// Detects using System.Reflection directives or reflection-related APIs in string literals.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReflectionApiAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> ReflectionNamespaces = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.Reflection",
        "System.Reflection.Emit",
        "System.Reflection.Metadata");

    private static readonly ImmutableHashSet<string> ReflectionTypeNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "MethodInfo",
        "PropertyInfo",
        "FieldInfo",
        "ConstructorInfo",
        "ParameterInfo",
        "MemberInfo",
        "EventInfo",
        "TypeInfo",
        "Assembly",
        "Module",
        "RuntimeMethodHandle",
        "RuntimeFieldHandle",
        "RuntimeTypeHandle",
        "BindingFlags",
        "Binder",
        "MethodBase",
        "CustomAttributeData");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.LSG013);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(AnalyzeStringLiteral, SyntaxKind.StringLiteralExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInterpolatedString, SyntaxKind.InterpolatedStringExpression);
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;
        var namespaceName = usingDirective.Name?.ToString();

        if (namespaceName == null)
            return;

        foreach (var reflectionNs in ReflectionNamespaces)
        {
            if (namespaceName == reflectionNs ||
                namespaceName.StartsWith(reflectionNs + ".", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG013,
                    usingDirective.GetLocation(),
                    namespaceName));
                return;
            }
        }
    }

    private static void AnalyzeStringLiteral(SyntaxNodeAnalysisContext context)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        var value = literal.Token.ValueText;

        CheckStringForReflection(context, value, literal.GetLocation());
    }

    private static void AnalyzeInterpolatedString(SyntaxNodeAnalysisContext context)
    {
        var interpolated = (InterpolatedStringExpressionSyntax)context.Node;

        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                CheckStringForReflection(context, text.TextToken.ValueText, content.GetLocation());
            }
        }
    }

    private static void CheckStringForReflection(
        SyntaxNodeAnalysisContext context,
        string value,
        Location location)
    {
        // Check for reflection namespace usage in generated code
        foreach (var reflectionNs in ReflectionNamespaces)
        {
            if (value.Contains("using " + reflectionNs) ||
                value.Contains(reflectionNs + "."))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG013,
                    location,
                    reflectionNs));
                return;
            }
        }

        // Check for reflection type usage
        foreach (var typeName in ReflectionTypeNames)
        {
            // Check for type.GetType(), Assembly.GetExecutingAssembly() etc.
            if (value.Contains("." + typeName + "(") ||
                value.Contains(typeName + ".") ||
                value.Contains(".GetType()") ||
                value.Contains(".GetMethod(") ||
                value.Contains(".GetProperty(") ||
                value.Contains(".GetField(") ||
                value.Contains(".GetConstructor("))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.LSG013,
                    location,
                    typeName));
                return;
            }
        }
    }
}
