using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace lint4sg.Analyzers;

/// <summary>LSG022: Detects ambient inputs read by incremental generator implementations.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AmbientInputAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.LSG022);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var generators = GetIncrementalGenerators(startContext.Compilation);
            if (generators.IsEmpty)
                return;

            var candidates = new ConcurrentBag<(IMethodSymbol? Caller, string Api, Location Location)>();
            var calls = new ConcurrentBag<(IMethodSymbol Caller, IMethodSymbol Callee)>();
            var nestedFunctions = new ConcurrentBag<(IMethodSymbol Parent, IMethodSymbol Child)>();
            startContext.RegisterOperationAction(
                c => AnalyzeInvocation(c, candidates, calls),
                OperationKind.Invocation);
            startContext.RegisterOperationAction(
                c => AnalyzeMethodReference(c, calls),
                OperationKind.MethodReference);
            startContext.RegisterOperationAction(
                c => AnalyzeProperty(c, candidates),
                OperationKind.PropertyReference);
            startContext.RegisterOperationAction(
                c => AnalyzeCreation(c, candidates),
                OperationKind.ObjectCreation);
            startContext.RegisterSyntaxNodeAction(
                c => AnalyzeNestedFunction(c, nestedFunctions),
                SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.ParenthesizedLambdaExpression,
                SyntaxKind.AnonymousMethodExpression,
                SyntaxKind.LocalFunctionStatement);
            startContext.RegisterCompilationEndAction(c =>
                ReportReachableInputs(c, generators, candidates, calls, nestedFunctions));
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        ConcurrentBag<(IMethodSymbol? Caller, string Api, Location Location)> candidates,
        ConcurrentBag<(IMethodSymbol Caller, IMethodSymbol Callee)> calls)
    {
        var operation = (IInvocationOperation)context.Operation;
        var method = operation.TargetMethod;
        if (context.ContainingSymbol is IMethodSymbol caller)
            calls.Add((caller, method));

        var type = method.ContainingType;
        var isAmbient = IsFrameworkType(context.Compilation, type, "System.IO.File")
                || IsFrameworkType(context.Compilation, type, "System.IO.Directory")
                || IsFrameworkType(context.Compilation, type, "System.IO.DirectoryInfo")
                || IsFrameworkType(context.Compilation, type, "System.IO.FileInfo")
            || IsFrameworkType(context.Compilation, type, "System.Environment")
                && method.Name is "GetEnvironmentVariable" or "GetEnvironmentVariables" or "ExpandEnvironmentVariables" or "GetCommandLineArgs"
            || IsFrameworkType(context.Compilation, type, "System.Guid") && method.Name == "NewGuid"
            || IsFrameworkType(context.Compilation, type, "System.Diagnostics.Process")
                && method.Name is "Start" or "GetCurrentProcess" or "GetProcesses" or "GetProcessById"
            || IsFrameworkType(context.Compilation, type, "System.Diagnostics.Stopwatch")
                && method.Name is "GetTimestamp" or "GetElapsedTime"
            || IsFrameworkType(context.Compilation, type, "System.Net.Http.HttpClient")
                && (method.Name.StartsWith("Get", StringComparison.Ordinal)
                    || method.Name.StartsWith("Send", StringComparison.Ordinal)
                    || method.Name.StartsWith("Post", StringComparison.Ordinal)
                    || method.Name.StartsWith("Put", StringComparison.Ordinal)
                    || method.Name.StartsWith("Delete", StringComparison.Ordinal))
            || IsFrameworkType(context.Compilation, type, "System.Net.WebClient")
                && (method.Name.Contains("Download", StringComparison.Ordinal)
                    || method.Name.Contains("Upload", StringComparison.Ordinal))
            || IsRandomType(context.Compilation, type)
                && method.Name is "Next" or "NextBytes" or "NextDouble" or "NextInt64"
                    or "GetInt32" or "GetBytes" or "GetNonZeroBytes" or "Fill";

        if (isAmbient)
            candidates.Add((context.ContainingSymbol as IMethodSymbol, method.Name, operation.Syntax.GetLocation()));
    }

    private static void AnalyzeMethodReference(
        OperationAnalysisContext context,
        ConcurrentBag<(IMethodSymbol Caller, IMethodSymbol Callee)> calls)
    {
        if (context.ContainingSymbol is IMethodSymbol caller
            && context.Operation is IMethodReferenceOperation methodReference)
            calls.Add((caller, methodReference.Method));
    }

    private static void AnalyzeProperty(
        OperationAnalysisContext context,
        ConcurrentBag<(IMethodSymbol? Caller, string Api, Location Location)> candidates)
    {
        var property = ((IPropertyReferenceOperation)context.Operation).Property;
        var type = property.ContainingType;
        var isDateTime = IsFrameworkType(context.Compilation, type, "System.DateTime")
            || IsFrameworkType(context.Compilation, type, "System.DateTimeOffset");
        var ambient = isDateTime
                && property.Name is "Now" or "UtcNow" or "Today"
            || IsFrameworkType(context.Compilation, type, "System.Environment")
                && property.Name is "CurrentDirectory" or "MachineName" or "ProcessorCount"
                    or "SystemDirectory" or "TickCount" or "TickCount64" or "CommandLine"
                    or "OSVersion" or "Version" or "UserName" or "UserDomainName"
                    or "ProcessId" or "CurrentManagedThreadId"
            || (IsFrameworkType(context.Compilation, type, "System.IO.FileInfo")
                || IsFrameworkType(context.Compilation, type, "System.IO.DirectoryInfo")
                || IsFrameworkType(context.Compilation, type, "System.IO.FileSystemInfo"))
                && property.Name is "Exists" or "Length" or "Attributes" or "CreationTime"
                    or "CreationTimeUtc" or "LastAccessTime" or "LastAccessTimeUtc"
                    or "LastWriteTime" or "LastWriteTimeUtc"
            || IsFrameworkType(context.Compilation, type, "System.Diagnostics.Process")
                && property.Name is "Id" or "StartTime" or "WorkingSet64" or "ProcessName"
                    or "Modules" or "Threads" or "MainModule" or "Responding";

        if (ambient)
            candidates.Add((context.ContainingSymbol as IMethodSymbol, property.Name, context.Operation.Syntax.GetLocation()));
    }

    private static void AnalyzeCreation(
        OperationAnalysisContext context,
        ConcurrentBag<(IMethodSymbol? Caller, string Api, Location Location)> candidates)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Constructor is { } constructor
            && (IsRandomType(context.Compilation, constructor.ContainingType)
                || IsFrameworkType(context.Compilation, constructor.ContainingType, "System.IO.FileStream")
                || IsFrameworkType(context.Compilation, constructor.ContainingType, "System.IO.StreamReader")
                || IsFrameworkType(context.Compilation, constructor.ContainingType, "System.IO.FileSystemWatcher")))
            candidates.Add((context.ContainingSymbol as IMethodSymbol, constructor.ContainingType.Name, creation.Syntax.GetLocation()));
    }

    private static void ReportReachableInputs(
        CompilationAnalysisContext context,
        ImmutableArray<INamedTypeSymbol> generators,
        ConcurrentBag<(IMethodSymbol? Caller, string Api, Location Location)> candidates,
        ConcurrentBag<(IMethodSymbol Caller, IMethodSymbol Callee)> calls,
        ConcurrentBag<(IMethodSymbol Parent, IMethodSymbol Child)> nestedFunctions)
    {
        var reachable = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IMethodSymbol>();
        foreach (var generator in generators)
        {
            foreach (var method in generator.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.Name is "Initialize" or ".ctor")
                    pending.Push(method);
            }
        }

        var edges = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (var (caller, callee) in calls)
        {
            if (!edges.TryGetValue(caller, out var callees))
                edges.Add(caller, callees = new List<IMethodSymbol>());
            callees.Add(callee);
        }

        var nestedEdges = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (var (parent, child) in nestedFunctions)
        {
            if (!nestedEdges.TryGetValue(parent, out var children))
                nestedEdges.Add(parent, children = new List<IMethodSymbol>());
            children.Add(child);
        }

        while (pending.Count > 0)
        {
            var method = pending.Pop();
            if (!reachable.Add(method))
                continue;
            if (edges.TryGetValue(method, out var callees))
                foreach (var callee in callees)
                    pending.Push(callee);
            if (nestedEdges.TryGetValue(method, out var children))
                foreach (var child in children)
                    pending.Push(child);
        }

        foreach (var (caller, api, location) in candidates)
        {
            if (caller != null && reachable.Contains(caller))
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.LSG022, location, api));
        }
    }

    private static void AnalyzeNestedFunction(
        SyntaxNodeAnalysisContext context,
        ConcurrentBag<(IMethodSymbol Parent, IMethodSymbol Child)> edges)
    {
        var node = context.Node;
        var child = node switch
        {
            AnonymousFunctionExpressionSyntax lambda => context.SemanticModel.GetSymbolInfo(lambda, context.CancellationToken).Symbol as IMethodSymbol,
            LocalFunctionStatementSyntax local => context.SemanticModel.GetDeclaredSymbol(local, context.CancellationToken) as IMethodSymbol,
            _ => null,
        };
        var parentNode = node.Ancestors().FirstOrDefault(n =>
            n is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax);
        var parent = parentNode switch
        {
            AnonymousFunctionExpressionSyntax lambda => context.SemanticModel.GetSymbolInfo(lambda, context.CancellationToken).Symbol as IMethodSymbol,
            LocalFunctionStatementSyntax local => context.SemanticModel.GetDeclaredSymbol(local, context.CancellationToken) as IMethodSymbol,
            BaseMethodDeclarationSyntax method => context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) as IMethodSymbol,
            _ => null,
        };
        if (child != null && parent != null)
            edges.Add((parent, child));
    }

    private static bool IsRandomType(Compilation compilation, INamedTypeSymbol type) =>
        IsFrameworkType(compilation, type, "System.Random")
        || IsFrameworkType(compilation, type, "System.Security.Cryptography.RandomNumberGenerator");

    private static bool IsFrameworkType(Compilation compilation, INamedTypeSymbol type, string metadataName)
    {
        var frameworkType = compilation.GetTypeByMetadataName(metadataName);
        return frameworkType != null
            && !SymbolEqualityComparer.Default.Equals(frameworkType.ContainingAssembly, compilation.Assembly)
            && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, frameworkType);
    }

    private static ImmutableArray<INamedTypeSymbol> GetIncrementalGenerators(Compilation compilation)
    {
        var interfaceSymbol = compilation.GetTypeByMetadataName("Microsoft.CodeAnalysis.IIncrementalGenerator");
        if (interfaceSymbol == null)
            return ImmutableArray<INamedTypeSymbol>.Empty;

        var result = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        AddGenerators(compilation.Assembly.GlobalNamespace, interfaceSymbol, result);
        return result.ToImmutable();
    }

    private static void AddGenerators(
        INamespaceSymbol @namespace,
        INamedTypeSymbol interfaceSymbol,
        ImmutableArray<INamedTypeSymbol>.Builder result)
    {
        foreach (var type in @namespace.GetTypeMembers())
            if (type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceSymbol)))
                result.Add(type);
        foreach (var child in @namespace.GetNamespaceMembers())
            AddGenerators(child, interfaceSymbol, result);
    }
}
