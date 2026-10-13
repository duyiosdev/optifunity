using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module1
{
    public static class CallGraphAnalyzer
    {
        // shortcut: static same-compilation calls only; use whole-program dispatch analysis before claiming complete reachability or leaks.
        public static List<PerformanceIssue> Analyze(string path, string source)
        {
            var model = SemanticCodeAnalyzer.Model(path, source);
            var result = new List<PerformanceIssue>();
            foreach (var method in model.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(method);
                if (!IsCallback(symbol)) continue;
                var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { symbol.OriginalDefinition };
                var pending = new Queue<(IMethodSymbol method, string chain)>();
                AddCalls(method, model, symbol.Name, pending);
                while (pending.Count > 0)
                {
                    var next = pending.Dequeue();
                    if (!visited.Add(next.method.OriginalDefinition)) continue;
                    if (visited.Count > 512)
                    {
                        result.Add(Issue(path, method, "code.callgraph", "Call graph incomplete", "Traversal limit 512 methods reached; remaining paths unexamined.")); break;
                    }
                    if (next.method.IsVirtual || next.method.IsAbstract || next.method.ContainingType.TypeKind == TypeKind.Interface) continue;
                    foreach (var declaration in next.method.DeclaringSyntaxReferences)
                    {
                        if (!model.Compilation.ContainsSyntaxTree(declaration.SyntaxTree)) continue;
                        var body = declaration.GetSyntax();
                        var nestedModel = model.Compilation.GetSemanticModel(declaration.SyntaxTree);
                        foreach (var node in DirectBody(body))
                        {
                            bool allocation = node is ArrayCreationExpressionSyntax || node is ImplicitArrayCreationExpressionSyntax ||
                                ((node is ObjectCreationExpressionSyntax || node is ImplicitObjectCreationExpressionSyntax) && nestedModel.GetTypeInfo(node).Type?.IsReferenceType == true);
                            if (allocation)
                                result.Add(Issue(path, method, "code.callgraph", "Allocation reachable through helper",
                                    next.chain + " at " + node.GetLocation().GetLineSpan() + "; static call path, execution frequency/branches not proven."));
                        }
                        AddCalls(body, nestedModel, next.chain, pending);
                    }
                }
            }
            foreach (var assignment in model.SyntaxTree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!assignment.IsKind(SyntaxKind.AddAssignmentExpression) || !(model.GetSymbolInfo(assignment.Left).Symbol is IEventSymbol subscription)) continue;
                var handler = model.GetSymbolInfo(assignment.Right).Symbol;
                var containingType = assignment.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (containingType == null) continue;
                bool cleanup = containingType.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(a =>
                    a.IsKind(SyntaxKind.SubtractAssignmentExpression) && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(a.Left).Symbol, subscription) &&
                    handler != null && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(a.Right).Symbol, handler));
                result.Add(Issue(path, assignment, "code.ownership", "Event ownership review",
                    "Publisher=" + subscription.ContainingType.ToDisplayString() + "; static=" + subscription.IsStatic +
                    "; matching handler removal in same type=" + cleanup + ". Receiver identity, cleanup reachability and lifetime are not proven; this is not a leak verdict."));
            }
            return result;
        }
        private static IEnumerable<SyntaxNode> DirectBody(SyntaxNode body) => body.DescendantNodes(n =>
            !(n is AnonymousFunctionExpressionSyntax) && !(n is LocalFunctionStatementSyntax));
        private static void AddCalls(SyntaxNode body, SemanticModel model, string chain, Queue<(IMethodSymbol, string)> queue)
        {
            foreach (var call in DirectBody(body).OfType<InvocationExpressionSyntax>())
                if (model.GetSymbolInfo(call).Symbol is IMethodSymbol symbol && symbol.DeclaringSyntaxReferences.Length > 0)
                    queue.Enqueue((symbol, chain + " -> " + symbol.ToDisplayString()));
        }
        private static bool IsCallback(IMethodSymbol symbol)
        {
            if (symbol == null || symbol.IsStatic || !symbol.ReturnsVoid || symbol.Parameters.Length != 0 ||
                !new[] { "Update", "LateUpdate", "FixedUpdate", "OnGUI" }.Contains(symbol.Name)) return false;
            for (var type = symbol.ContainingType; type != null; type = type.BaseType)
                if (type.ToDisplayString() == "UnityEngine.MonoBehaviour") return true;
            return false;
        }
        private static PerformanceIssue Issue(string path, SyntaxNode node, string rule, string title, string description) =>
            new PerformanceIssue { RuleId = rule, EvidenceLevel = "Advisory", Severity = IssueSeverity.Info, AssetPath = path,
                CodeLocation = path + ":L" + (node.GetLocation().GetLineSpan().StartLinePosition.Line + 1), Title = title,
                Description = description, FixSuggestion = "Inspect the call path and object ownership, then measure on the target device." };
    }
}
