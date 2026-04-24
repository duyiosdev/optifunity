using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Optifunity.Editor.Module1
{
    /// <summary>
    /// Điều phối toàn bộ quá trình phân tích mã nguồn C#:
    /// tìm tất cả scripts, chạy các bộ analyzer (Regex-based), tổng hợp về ReportEngine.
    /// </summary>
    public static class CodeAnalysisRunner
    {
        private static bool _isRunning;

        public static List<Core.PerformanceIssue> RunAll(bool showProgress = true, bool myScriptsOnly = false)
        {
            if (_isRunning)
            {
                Debug.LogWarning("[Optifunity] Code analysis đang chạy, vui lòng đợi.");
                return new List<Core.PerformanceIssue>();
            }

            _isRunning = true;
            var allIssues = new List<Core.PerformanceIssue>();

            try
            {
                string[] guids = AssetDatabase.FindAssets("t:Script", new[] { "Assets" });
                int total = guids.Length;

                for (int idx = 0; idx < total; idx++)
                {
                    string guid      = guids[idx];
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                    if (assetPath.Contains("/Packages/") || assetPath.Contains("\\Packages\\") || assetPath.Contains(".g.cs"))
                        continue;

                    if (myScriptsOnly && !UI.DashboardWindow.IsUserCodePath(assetPath))
                        continue;

                    if (showProgress)
                    {
                        float progress = (float)idx / total;
                        bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                            "Optifunity — Code Analysis",
                            $"Phân tích: {Path.GetFileName(assetPath)}",
                            progress);
                        if (cancelled) break;
                    }

                    string fullPath = Path.GetFullPath(assetPath);
                    if (!File.Exists(fullPath)) continue;

                    string[] lines;
                    try { lines = File.ReadAllLines(fullPath); }
                    catch { continue; }

                    // ─── Chạy các bộ phân tích (Regex-based) ───────────────────────────
                    var gcIssues     = GCAllocAnalyzer.Analyze(fullPath, lines);
                    var boxIssues    = BoxingAnalyzer.Analyze(fullPath, lines);
                    var leakIssues   = MemLeakAnalyzer.Analyze(fullPath, lines);
                    var antiIssues   = PerformanceAntiPatternAnalyzer.Analyze(fullPath, lines);
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
                }
            }
            finally
            {
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
