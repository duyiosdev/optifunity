using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module8;

namespace Optifunity.Editor.UI
{
    /// <summary>
    /// Cửa sổ Báo Cáo chi tiết — hiển thị toàn bộ issues có thể filter/sort/export.
    /// Menu: Tools > Optifunity > Report Viewer
    /// </summary>
    public class ReportWindow : EditorWindow
    {
        private Vector2       _scrollPos;
        private string        _searchFilter  = "";
        private IssueSeverity? _filterSeverity = null;
        private IssueModule?  _filterModule   = null;
        private int           _sortMode       = 0; // 0=Severity, 1=Module, 2=Title

        private readonly string[] _sortLabels = { "Sort: Severity", "Sort: Module", "Sort: Title" };

        [MenuItem("Tools/Optifunity/Report Viewer", priority = 50)]
        public static void ShowWindow()
        {
            var window = GetWindow<ReportWindow>("Optifunity Report");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), OptifunityStyles.BgDark);

            var report = ReportEngine.LastReport;

            DrawFilterBar(report);
            OptifunityStyles.DrawSeparator();

            if (report == null)
            {
                GUILayout.Space(40);
                GUILayout.Label("Chưa có dữ liệu. Mở Dashboard và chạy Full Scan.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 });
                return;
            }

            DrawReportHeader(report);
            OptifunityStyles.DrawSeparator();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            DrawFilteredIssues(report);
            EditorGUILayout.EndScrollView();
        }

        private void DrawFilterBar(ScanReport report)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                GUILayout.Label("🔍", GUILayout.Width(18));
                _searchFilter = EditorGUILayout.TextField(_searchFilter, GUILayout.Width(200));

                GUILayout.Space(8);

                // Severity filter
                GUILayout.Label("Severity:", GUILayout.Width(55));
                if (GUILayout.Toggle(_filterSeverity == null, "All", "Button", GUILayout.Width(35)))
                    _filterSeverity = null;
                if (GUILayout.Toggle(_filterSeverity == IssueSeverity.Error,   "ERR",  "Button", GUILayout.Width(35)))
                    _filterSeverity = IssueSeverity.Error;
                if (GUILayout.Toggle(_filterSeverity == IssueSeverity.Warning, "WARN", "Button", GUILayout.Width(40)))
                    _filterSeverity = IssueSeverity.Warning;
                if (GUILayout.Toggle(_filterSeverity == IssueSeverity.Info,    "INFO", "Button", GUILayout.Width(35)))
                    _filterSeverity = IssueSeverity.Info;

                GUILayout.Space(8);
                _sortMode = EditorGUILayout.Popup(_sortMode, _sortLabels, GUILayout.Width(120));

                GUILayout.FlexibleSpace();

                // Export
                if (report != null && GUILayout.Button("Export MD", GUILayout.Width(80)))
                {
                    string path = ReportEngine.SaveMarkdownReport();
                    EditorUtility.RevealInFinder(path);
                }

                GUILayout.Space(8);
            }
        }

        private void DrawReportHeader(ScanReport report)
        {
            using (new EditorGUILayout.HorizontalScope(OptifunityStyles.StyleCard))
            {
                GUILayout.Label($"Scan: {report.ScanTime:yyyy-MM-dd HH:mm}", OptifunityStyles.StyleSubtitle);
                GUILayout.Space(10);
                GUILayout.Label($"Platform: {report.BudgetSnapshot.Platform} ({report.BudgetSnapshot.PhysicalRamMB}MB)", OptifunityStyles.StyleSubtitle);
                GUILayout.FlexibleSpace();

                OptifunityStyles.DrawBadge($"{report.ErrorCount} Errors",   OptifunityStyles.ColorError);
                OptifunityStyles.DrawBadge($"{report.WarningCount} Warnings", OptifunityStyles.ColorWarning);
                OptifunityStyles.DrawBadge($"{report.InfoCount} Infos",      OptifunityStyles.ColorInfo);

                GUILayout.Space(10);
                Color hc = OptifunityStyles.GetHealthColor(report.HealthScore);
                GUILayout.Label($"Health: {report.HealthScore}/100",
                    new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = hc } });
            }
        }

        private void DrawFilteredIssues(ScanReport report)
        {
            var issues = report.AllIssues.AsEnumerable();

            // Apply filters
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                string f = _searchFilter.ToLower();
                issues = issues.Where(i =>
                    (i.Title       ?? "").ToLower().Contains(f) ||
                    (i.Description ?? "").ToLower().Contains(f) ||
                    (i.AssetPath   ?? "").ToLower().Contains(f));
            }

            if (_filterSeverity.HasValue)
                issues = issues.Where(i => i.Severity == _filterSeverity.Value);

            if (_filterModule.HasValue)
                issues = issues.Where(i => i.Module == _filterModule.Value);

            // Apply sort
            issues = _sortMode switch
            {
                1 => issues.OrderBy(i => i.Module).ThenByDescending(i => i.Severity),
                2 => issues.OrderBy(i => i.Title),
                _ => issues.OrderByDescending(i => i.Severity).ThenBy(i => i.Module)
            };

            var list = issues.ToList();
            GUILayout.Space(4);
            GUILayout.Label($"  Hiển thị {list.Count} / {report.TotalCount} issues",
                OptifunityStyles.StyleSubtitle);
            GUILayout.Space(4);

            foreach (var issue in list)
                DrawDetailedIssueRow(issue);
        }

        private void DrawDetailedIssueRow(PerformanceIssue issue)
        {
            var bg = OptifunityStyles.GetSeverityBg(issue.Severity);
            using (new EditorGUILayout.VerticalScope(new GUIStyle(OptifunityStyles.StyleCard)
                   { normal = { background = bg }, margin = new RectOffset(8, 8, 2, 2) }))
            {
                // Header row
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Severity
                    GUILayout.Label(OptifunityStyles.GetSeverityIcon(issue.Severity),
                        OptifunityStyles.GetSeverityStyle(issue.Severity), GUILayout.Width(14));

                    // Title
                    GUILayout.Label(issue.Title, OptifunityStyles.StyleIssueTitle);
                    GUILayout.FlexibleSpace();

                    // Module + Severity badges
                    OptifunityStyles.DrawBadge(issue.Module.ToString(), OptifunityStyles.TextSecondary);
                    OptifunityStyles.DrawBadge(issue.Severity.ToString(),
                        issue.Severity == IssueSeverity.Error   ? OptifunityStyles.ColorError :
                        issue.Severity == IssueSeverity.Warning ? OptifunityStyles.ColorWarning :
                        OptifunityStyles.ColorInfo);

                    // Auto-Fix
                    if (issue.CanAutoFix && issue.AutoFixAction != null)
                    {
                        if (GUILayout.Button(issue.AutoFixLabel ?? "Auto-Fix",
                            OptifunityStyles.StyleButtonAutoFix, GUILayout.Width(90)))
                        {
                            issue.AutoFixAction();
                        }
                    }

                    // Open asset
                    if (!string.IsNullOrEmpty(issue.AssetPath) || !string.IsNullOrEmpty(issue.CodeLocation))
                    {
                        if (GUILayout.Button("→ Open", GUILayout.Width(55), GUILayout.Height(18)))
                        {
                            if (issue.Module == IssueModule.SceneRendererAudit)
                            {
                                var choice = EditorUtility.DisplayDialogComplex(
                                    "Optifunity",
                                    "Chọn kiểu locate cho Scene issue:",
                                    "GameObject",
                                    "Hủy",
                                    "LODGroup");

                                if (choice == 0)
                                {
                                    bool located = SceneRendererAuditRunner.TryLocateGameObject(issue);
                                    if (!located)
                                        EditorUtility.DisplayDialog("Optifunity", "Không locate được GameObject từ issue này. Hãy chạy lại Scene Scan để refresh token object path.", "OK");
                                }
                                else if (choice == 2)
                                {
                                    bool locatedLod = SceneRendererAuditRunner.TryLocateReferencingLodGroup(issue);
                                    if (!locatedLod)
                                        EditorUtility.DisplayDialog("Optifunity", "Không tìm thấy LODGroup đang reference renderer này.", "OK");
                                }
                            }
                            else if (!string.IsNullOrEmpty(issue.AssetPath))
                            {
                                var obj = AssetDatabase.LoadAssetAtPath<Object>(issue.AssetPath);
                                if (obj != null) { EditorGUIUtility.PingObject(obj); Selection.activeObject = obj; }
                            }
                        }
                    }
                }

                // Asset / Code location
                if (!string.IsNullOrEmpty(issue.AssetPath))
                    GUILayout.Label($"  📁 {issue.AssetPath}", OptifunityStyles.StyleSubtitle);
                if (!string.IsNullOrEmpty(issue.CodeLocation))
                    GUILayout.Label($"  📍 {issue.CodeLocation}", OptifunityStyles.StyleSubtitle);

                // Description
                if (!string.IsNullOrEmpty(issue.Description))
                    GUILayout.Label(issue.Description, OptifunityStyles.StyleIssueDesc);

                // Fix suggestion
                if (!string.IsNullOrEmpty(issue.FixSuggestion))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("💡", GUILayout.Width(16));
                        GUILayout.Label(issue.FixSuggestion,
                            new GUIStyle(OptifunityStyles.StyleIssueDesc)
                            {
                                normal = { textColor = new Color(0.7f, 0.85f, 0.65f) }
                            });
                    }
                }
            }
        }
    }
}
