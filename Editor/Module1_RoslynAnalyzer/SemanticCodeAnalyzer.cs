using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Optifunity.Editor.Core;
using UnityEditor.Compilation;

namespace Optifunity.Editor.Module1
{
    public static class SemanticCodeAnalyzer
    {
        private static readonly Dictionary<string, SemanticModel> Models = new Dictionary<string, SemanticModel>();
        private static readonly Dictionary<string, CSharpCompilation> Compilations = new Dictionary<string, CSharpCompilation>();
        public static void Clear() { Models.Clear(); Compilations.Clear(); }

        private static SemanticModel Model(string path, string source)
        {
            string fullPath = Path.GetFullPath(path);
            string key = fullPath + "\n" + source;
            if (Models.TryGetValue(key, out var cached)) return cached;
            var assembly = CompilationPipeline.GetAssemblies().FirstOrDefault(a =>
                a.sourceFiles.Any(f => Path.GetFullPath(f) == fullPath));
            if (assembly != null && Compilations.TryGetValue(assembly.name, out var existing))
            {
                var existingTree = existing.SyntaxTrees.FirstOrDefault(t => t.FilePath == fullPath);
                if (existingTree != null && existingTree.GetText().ToString() == source)
                    return existing.GetSemanticModel(existingTree);
            }
            var options = new CSharpParseOptions(LanguageVersion.CSharp9,
                preprocessorSymbols: assembly?.defines ?? Array.Empty<string>());
            var tree = CSharpSyntaxTree.ParseText(source, options, fullPath);
            var trees = new List<SyntaxTree> { tree };
            if (assembly != null)
                foreach (var file in assembly.sourceFiles)
                    if (Path.GetFullPath(file) != fullPath && File.Exists(file))
                        trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file).Replace("\r\n", "\n"), options, Path.GetFullPath(file)));
            var paths = assembly != null ? assembly.compiledAssemblyReferences.Concat(assembly.assemblyReferences.Select(a => a.outputPath)).ToArray() :
                AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.Location).ToArray();
            var references = new List<MetadataReference>();
            foreach (var reference in paths.Distinct())
                if (File.Exists(reference))
                    try { references.Add(MetadataReference.CreateFromFile(reference)); }
                    catch (BadImageFormatException) { }
            var compilation = CSharpCompilation.Create("OptifunityInspection", trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            if (assembly != null) Compilations[assembly.name] = compilation;
            var model = compilation.GetSemanticModel(tree);
            // Keep only the currently analyzed file; full-project compilations must not accumulate per script.
            Models.Clear(); Models[key] = model;
            return model;
        }

        public static List<PerformanceIssue> Analyze(string path, string[] lines, string group)
        {
            var result = new List<PerformanceIssue>();
            SemanticModel model;
            try { model = Model(path, string.Join("\n", lines)); }
            catch (Exception ex)
            {
                result.Add(new PerformanceIssue { RuleId = "code.unresolved", EvidenceLevel = "Advisory",
                    Severity = IssueSeverity.Info, Title = "Semantic analysis unavailable", Description = ex.Message,
                    CodeLocation = path, FixSuggestion = "Resolve compilation references and scan again." });
                return result;
            }
            var root = model.SyntaxTree.GetRoot();
            if (group == "Allocation")
            {
                var errors = model.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(3).ToArray();
                if (errors.Length > 0)
                    result.Add(new PerformanceIssue { RuleId = "code.unresolved", EvidenceLevel = "Advisory", Severity = IssueSeverity.Info,
                        Title = "Incomplete semantic context", CodeLocation = path,
                        Description = string.Join("\n", errors.Select(d => d.ToString())),
                        FixSuggestion = "Resolve compilation diagnostics; findings describe only operations with resolved types, not full coverage." });
            }
            void Add(SyntaxNode node, string id, string title, string description, string fix, bool advisory = false)
            {
                result.Add(new PerformanceIssue { RuleId = id, EvidenceLevel = advisory ? "Advisory" : "Confirmed static operation",
                    Severity = advisory ? IssueSeverity.Info : IssueSeverity.Warning, Title = title,
                    Description = description, FixSuggestion = fix, AssetPath = path,
                    CodeLocation = path + ":L" + (node.GetLocation().GetLineSpan().StartLinePosition.Line + 1) });
            }
            foreach (var node in root.DescendantNodes())
            {
                if (group == "Allocation" && InGameplayMethod(node, model))
                {
                    if (node is ArrayCreationExpressionSyntax || node is ImplicitArrayCreationExpressionSyntax)
                        Add(node, "code.allocation", "Cấp phát mảng mới trong gameplay method", "Array creation is a managed allocation when executed; frequency and impact require profiling.", "Reuse a buffer if lifetime and ownership permit.");
                    else if (node is ObjectCreationExpressionSyntax || node is ImplicitObjectCreationExpressionSyntax)
                    {
                        var type = model.GetTypeInfo(node).Type;
                        if (type != null && type.TypeKind != TypeKind.Error && type.IsReferenceType)
                            Add(node, "code.allocation", "Cấp phát đối tượng mới trong gameplay method", "Reference-type construction when this expression executes; no measured frame impact implied.", "Measure allocation and consider reuse or pooling.");
                        else if (type == null || type.TypeKind == TypeKind.Error)
                            Add(node, "code.unresolved", "Unresolved construction type", "Cannot determine value/reference semantics.", "Fix compilation context and rescan.", true);
                    }
                    else if (node is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression) &&
                        model.GetTypeInfo(binary).Type?.SpecialType == SpecialType.System_String && !model.GetConstantValue(binary).HasValue)
                        Add(node, "code.allocation", "String concatenation trong gameplay method", "Runtime string concatenation may allocate; constant folding has been excluded.", "Avoid repeated construction; measure any replacement.");
                }
                if (group == "Boxing" && node is ExpressionSyntax expression)
                {
                    bool boxed = node is CastExpressionSyntax cast
                        ? model.GetTypeInfo(cast.Type).Type != null && model.ClassifyConversion(cast.Expression, model.GetTypeInfo(cast.Type).Type).IsBoxing
                        : model.GetConversion(expression).IsBoxing;
                    if (boxed)
                        Add(node, "code.boxing", "Boxing conversion", "The compiler resolves a value-type to reference/interface conversion. Runtime cost depends on execution frequency.", "Consider a generic API or typed storage, then profile.");
                }
                if (group == "Lifetime" && node is AssignmentExpressionSyntax assignment &&
                    assignment.IsKind(SyntaxKind.AddAssignmentExpression) && model.GetSymbolInfo(assignment.Left).Symbol is IEventSymbol)
                    Add(node, "code.event-lifetime", "Review event subscription lifetime", "Event subscription confirmed; this is not proof of a leak. Publisher lifetime, GC roots and cleanup paths require review.", "Compare publisher/subscriber lifetime and unsubscribe where required.", true);
                if (group == "Resource" && node is InvocationExpressionSyntax call && model.GetSymbolInfo(call).Symbol is IMethodSymbol method)
                {
                    if (method.ContainingType.ToDisplayString() == "UnityEngine.Resources" && (method.Name == "Load" || method.Name == "LoadAll"))
                        Add(node, "code.resources", "Synchronous Resources load", "Synchronous API call found; stalls depend on asset state and execution context.", "Measure loading and consider asynchronous loading.", true);
                    if (method.ContainingType.ToDisplayString() == "UnityEngine.Object" && (method.Name == "Instantiate" || method.Name == "Destroy") &&
                        node.Ancestors().Any(a => a is ForStatementSyntax || a is ForEachStatementSyntax || a is WhileStatementSyntax || a is DoStatementSyntax))
                        Add(node, "code.resources", "Instantiate/Destroy in loop", "Resolved Unity object operation inside a syntactic loop.", "Profile cost and consider pooling when appropriate.", true);
                }
            }
            return result;
        }

        private static bool InGameplayMethod(SyntaxNode node, SemanticModel model)
        {
            // Nested local functions and lambdas may run later; do not attribute their bodies to the enclosing callback.
            var owner = node.Ancestors().FirstOrDefault(a => a is MethodDeclarationSyntax || a is LocalFunctionStatementSyntax || a is AnonymousFunctionExpressionSyntax);
            if (!(owner is MethodDeclarationSyntax method) || method.ParameterList.Parameters.Count != 0 ||
                !new[] { "Update", "FixedUpdate", "LateUpdate", "OnGUI" }.Contains(method.Identifier.ValueText)) return false;
            var symbol = model.GetDeclaredSymbol(method);
            if (symbol == null || symbol.IsStatic || !symbol.ReturnsVoid) return false;
            for (var type = symbol.ContainingType; type != null; type = type.BaseType)
                if (type.ToDisplayString() == "UnityEngine.MonoBehaviour") return true;
            return false;
        }
    }
}
