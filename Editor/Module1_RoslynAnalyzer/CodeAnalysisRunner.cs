using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Optifunity.Editor.Module1
{
    /// <summary>
    /// Điều phối toàn bộ quá trình phân tích mã nguồn C#:
    /// tìm tất cả scripts, chạy các bộ analyzer (Roslyn semantic), tổng hợp về ReportEngine.
    /// </summary>
    public static class CodeAnalysisRunner
    {
        private static bool _isRunning;
        public static string LastCoverage { get; private set; } = "Not scanned";

        public static List<Core.PerformanceIssue> RunAll(bool showProgress = true, bool myScriptsOnly = false, System.Func<bool> shouldCancel = null, bool includeCallGraph = false)
        {
            if (_isRunning)
            {
                Debug.LogWarning("[Optifunity] Code analysis đang chạy, vui lòng đợi.");
                return new List<Core.PerformanceIssue>();
            }

            _isRunning = true;
            SemanticCodeAnalyzer.Clear();
            var allIssues = new List<Core.PerformanceIssue>();

            int scanned = 0, skipped = 0;
            bool incomplete = false;
            LastCoverage = "In progress";
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:Script", new[] { "Assets" });
                int total = guids.Length;

                for (int idx = 0; idx < total; idx++)
                {
                    string guid      = guids[idx];
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                    if (!assetPath.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase) || assetPath.Contains("/Packages/") || assetPath.Contains("\\Packages\\") || assetPath.Contains(".g.cs"))
                        continue;

                    if (myScriptsOnly && !UI.DashboardWindow.IsUserCodePath(assetPath))
                    { skipped++; continue; }
                    if (shouldCancel?.Invoke() == true) { incomplete = true; break; }

                    if (showProgress)
                    {
                        float progress = (float)idx / total;
                        bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                            "Optifunity — Code Analysis",
                            $"Phân tích: {Path.GetFileName(assetPath)}",
                            progress);
                        if (cancelled) { incomplete = true; break; }
                    }

                    string fullPath = Path.GetFullPath(assetPath);
                    if (!File.Exists(fullPath)) { skipped++; incomplete = true; continue; }

                    string[] lines;
                    try { lines = File.ReadAllLines(fullPath); }
                    catch { skipped++; incomplete = true; continue; }

                    // ─── Chạy các bộ phân tích (Regex-based) ───────────────────────────
                    var gcIssues     = GCAllocAnalyzer.Analyze(fullPath, lines);
                    var boxIssues    = BoxingAnalyzer.Analyze(fullPath, lines);
                    var leakIssues   = MemLeakAnalyzer.Analyze(fullPath, lines);
                    var antiIssues   = new List<Core.PerformanceIssue>(); // Legacy lexical heuristics are not semantic evidence.
                    var resourceIssues = ResourceManagementAnalyzer.Analyze(fullPath, lines);

                    // Gán AssetPath cho tất cả issues
                    SetAssetPath(gcIssues,   assetPath);
                    SetAssetPath(boxIssues,  assetPath);
                    SetAssetPath(leakIssues, assetPath);
                    SetAssetPath(antiIssues, assetPath);
                    SetAssetPath(resourceIssues, assetPath);

                    allIssues.AddRange(gcIssues);
                    allIssues.AddRange(boxIssues);
                    allIssues.AddRange(leakIssues);
                    allIssues.AddRange(antiIssues);
                    allIssues.AddRange(resourceIssues);
                    if (includeCallGraph) allIssues.AddRange(CallGraphAnalyzer.Analyze(assetPath, string.Join("\n", lines)));
                    scanned++;
                }
            }
            catch { incomplete = true; throw; }
            finally
            {
                incomplete |= allIssues.Exists(i => i.RuleId == "code.unresolved" || i.Title == "Call graph incomplete");
                LastCoverage = $"{(incomplete ? "Incomplete" : "Complete within selected scope")}: Assets C#; MyScripts={myScriptsOnly}; scanned={scanned}; skipped={skipped}; Packages/generated sources excluded; callGraph={includeCallGraph} (static same-compilation paths only)";
                SemanticCodeAnalyzer.Clear();
                _isRunning = false;
                if (showProgress) EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[Optifunity] Code Analysis hoàn tất: {allIssues.Count} issues.");
            return allIssues;
        }

        private static void SetAssetPath(List<Core.PerformanceIssue> issues, string path)
        {
            foreach (var issue in issues)
            {
                if (string.IsNullOrEmpty(issue.AssetPath))
                    issue.AssetPath = path;
            }
        }

        public static void PingAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            var obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }
    }
}
