using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace lint4sg.Analyzers;

/// <summary>
/// LSG013: Reflection API usage in a source generator project.
/// Detects generator-side reflection semantically and reflection APIs embedded in generated source strings.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReflectionApiAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> ReflectionNamespaces = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.Reflection",
        "System.Reflection.Emit",
        "System.Reflection.Metadata"
    );

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
        "CustomAttributeData"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.LSG013);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterSyntaxNodeAction(AnalyzeStringLiteral, SyntaxKind.StringLiteralExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeInterpolatedString,
            SyntaxKind.InterpolatedStringExpression
        );
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var containingType = method.ContainingType;
        var objectType = context.Compilation.GetSpecialType(SpecialType.System_Object);
        var objectGetType = objectType.GetMembers("GetType").OfType<IMethodSymbol>().FirstOrDefault(m => m.Parameters.Length == 0);
        var systemType = context.Compilation.GetTypeByMetadataName("System.Type");
        var isSystemTypeApi = systemType != null
            && !SymbolEqualityComparer.Default.Equals(systemType.ContainingAssembly, context.Compilation.Assembly)
            && SymbolEqualityComparer.Default.Equals(containingType.OriginalDefinition, systemType);
        if (!IsReflectionType(context.Compilation, containingType)
            && !(objectGetType != null && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, objectGetType.OriginalDefinition)))
        {
            // System.Type itself is the reflection surface; any of its methods inspect or
            // manipulate runtime type metadata, even when the method name is unfamiliar.
            if (!isSystemTypeApi)
                return;
        }

        ReportSemantic(context, method.Name, invocation.Syntax.GetLocation());
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        var property = ((IPropertyReferenceOperation)context.Operation).Property;
        var systemType = context.Compilation.GetTypeByMetadataName("System.Type");
        var isSystemTypeProperty = systemType != null
            && !SymbolEqualityComparer.Default.Equals(systemType.ContainingAssembly, context.Compilation.Assembly)
            && SymbolEqualityComparer.Default.Equals(property.ContainingType.OriginalDefinition, systemType);
        if (!isSystemTypeProperty && !IsReflectionType(context.Compilation, property.ContainingType))
            return;

        ReportSemantic(context, property.Name, context.Operation.Syntax.GetLocation());
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Constructor is not { } constructor || !IsReflectionType(context.Compilation, constructor.ContainingType))
            return;

        ReportSemantic(context, constructor.ContainingType.Name, creation.Syntax.GetLocation());
    }

    private static void ReportSemantic(OperationAnalysisContext context, string api, Location location) =>
        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.LSG013, location, api));

    private static bool IsReflectionType(Compilation compilation, INamedTypeSymbol type)
    {
        foreach (var metadataName in ReflectionTypeMetadataNames)
        {
            var reflectionType = compilation.GetTypeByMetadataName(metadataName);
            if (reflectionType == null
                || SymbolEqualityComparer.Default.Equals(reflectionType.ContainingAssembly, compilation.Assembly))
                continue;

            for (var current = type; current != null; current = current.BaseType)
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, reflectionType)
                    || (ReflectionNamespaces.Contains(current.ContainingNamespace?.ToDisplayString() ?? string.Empty)
                        && !SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, compilation.Assembly)))
                    return true;
        }
        return false;
    }

    private static readonly string[] ReflectionTypeMetadataNames = ReflectionTypeNames
        .Select(static name => "System.Reflection." + name)
        .Concat(new[]
        {
            "System.Reflection.Emit.DynamicMethod",
            "System.Reflection.Emit.AssemblyBuilder",
            "System.Reflection.Emit.ModuleBuilder",
            "System.Reflection.Emit.TypeBuilder",
            "System.Reflection.Emit.MethodBuilder",
            "System.Reflection.Emit.ConstructorBuilder",
            "System.Reflection.Emit.PropertyBuilder",
            "System.Reflection.Emit.FieldBuilder",
            "System.Reflection.Emit.EventBuilder",
            "System.Reflection.Emit.ILGenerator",
            "System.Reflection.Emit.SignatureHelper",
            "System.Reflection.Emit.LocalBuilder",
        })
        .ToArray();

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
        Location location
    )
    {
        // Check for reflection namespace usage in generated code
        foreach (var reflectionNs in ReflectionNamespaces)
        {
            if (value.Contains("using " + reflectionNs) || value.Contains(reflectionNs + "."))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.LSG013, location, reflectionNs)
                );
                return;
            }
        }

        // Check for reflection type usage
        foreach (var typeName in ReflectionTypeNames)
        {
            // Check for type.GetType(), Assembly.GetExecutingAssembly() etc.
            if (
                value.Contains("." + typeName + "(")
                || value.Contains(typeName + ".")
                || value.Contains(".GetType()")
                || value.Contains(".GetMethod(")
                || value.Contains(".GetProperty(")
                || value.Contains(".GetField(")
                || value.Contains(".GetConstructor(")
            )
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.LSG013, location, typeName)
                );
                return;
            }
        }
    }
}
