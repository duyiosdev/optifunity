using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Optifunity.Editor.Core
{
    /// <summary>
    /// Phân loại mức độ nghiêm trọng của issue
    /// </summary>
    public enum IssueSeverity
    {
        Info    = 0,
        Warning = 1,
        Error   = 2
    }

    /// <summary>
    /// Phân loại module nguồn
    /// </summary>
    public enum IssueModule
    {
        CodeAnalysis,
        AssetAudit,
        URPDiagnostics,
        MobileWorkflow,
        SceneRendererAudit
    }

    /// <summary>
    /// Một vấn đề hiệu năng được phát hiện bởi plugin
    /// </summary>
    [Serializable]
    public class PerformanceIssue
    {
        public string RuleId = "legacy.unverified";
        public string EvidenceLevel = "Advisory";
        public IssueModule    Module;
        public IssueSeverity  Severity;
        public string         Title;
        public string         Description;
        public string         AssetPath;       // Đường dẫn asset liên quan (nếu có)
        public string         CodeLocation;    // File:Line:Column cho code issues
        public string         FindingIdentity; // Distinguishes separate static paths at the same source location.
        public string         FixSuggestion;   // Hướng dẫn khắc phục
        public bool           CanAutoFix { get => false; set { } }      // Có thể tự động sửa không
        public string         AutoFixLabel;    // Nhãn nút Auto-Fix
        public System.Action  AutoFixAction { get => null; set { } }   // Hành động Auto-Fix (runtime)

        public override string ToString() =>
            $"[{Severity}][{Module}] {Title} — {AssetPath ?? CodeLocation ?? ""}";
    }

    /// <summary>
    /// Kết quả tổng hợp từ một lần scan đầy đủ
    /// </summary>
    [Serializable]
    public class ScanReport
    {
        public DateTime     ScanTime;
        public string       UnityVersion;
        public string       ProjectName;
        public string Scope = "Assets and loaded scene/settings only; Packages, build dependencies and device runtime are not fully covered.";
        public string Completion = "In progress";
        public string Quality;
        public List<string> ModuleCoverage = new();
        public string Pipeline;
        public BudgetSummary BudgetSnapshot;

        public List<PerformanceIssue> CodeIssues      = new();
        public List<PerformanceIssue> AssetIssues     = new();
        public List<PerformanceIssue> URPIssues       = new();
        public List<PerformanceIssue> MobileIssues    = new();
        public List<PerformanceIssue> SceneRendererIssues = new();

        public IEnumerable<PerformanceIssue> AllIssues =>
            CodeIssues.Concat(AssetIssues).Concat(URPIssues).Concat(MobileIssues).Concat(SceneRendererIssues);

        public int ErrorCount   => AllIssues.Count(i => i.Severity == IssueSeverity.Error);
        public int WarningCount => AllIssues.Count(i => i.Severity == IssueSeverity.Warning);
        public int InfoCount    => AllIssues.Count(i => i.Severity == IssueSeverity.Info);
        public int TotalCount   => AllIssues.Count();

        /// <summary>
        /// Health Score 0-100 dựa trên số lượng và độ nghiêm trọng của issues
        /// </summary>
        public bool HasScoreEvidence => ModuleCoverage.Any(c => c.StartsWith("CodeAnalysis:") &&
            c.Contains("Complete within selected scope") && !c.Contains("scanned=0;"));
        public string ScoreLabel => HasScoreEvidence ? HealthScore + "/100 (selected code rules)" : "Not evaluated";

        public int HealthScore
        {
            get
            {
                if (TotalCount == 0) return 100;
                float penalty = ErrorCount * 10f + WarningCount * 3f;
                return Mathf.Max(0, Mathf.RoundToInt(100f - penalty));
            }
        }
    }

    /// <summary>
    /// Engine trung tâm: thu thập, tổng hợp issues từ 4 module và xuất báo cáo.
    /// </summary>
    public static class ReportEngine
    {
        private static ScanReport _lastReport;
        public  static ScanReport LastReport => _lastReport;

        public static event Action<ScanReport> OnScanCompleted;

        /// <summary>
        /// Khởi tạo một báo cáo scan mới (gọi trước khi chạy các module)
        /// </summary>
        public static ScanReport BeginScan()
        {
            _lastReport = new ScanReport
            {
                ScanTime       = DateTime.Now,
                UnityVersion   = Application.unityVersion,
                ProjectName    = Application.productName,
                Quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                Pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name ?? "Built-in",
                BudgetSnapshot = PlatformConfig.GetBudgetSummary()
            };
            return _lastReport;
        }

        /// <summary>
        /// Thêm issues từ một module vào báo cáo hiện tại
        /// </summary>
        public static ScanReport BeginModuleScan(IssueModule module)
        {
            if (_lastReport == null) BeginScan();
            _lastReport.Completion = "In progress: " + module;
            return _lastReport;
        }

        public static void AddIssues(IssueModule module, IEnumerable<PerformanceIssue> issues)
        {
            if (_lastReport == null) BeginScan();

            var list = issues?.ToList() ?? new List<PerformanceIssue>();
            foreach (var issue in list)
            {
                issue.Module = module;
                var rule = RuleCatalog.Get(issue.RuleId);
                issue.RuleId = rule.ruleId;
                if (rule.evidenceLevel == "Advisory") { issue.Severity = IssueSeverity.Info; issue.EvidenceLevel = "Advisory"; }
                if (issue.RuleId == "legacy.unverified") issue.Description = "UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.\n" + issue.Description;
            }

            _lastReport.ModuleCoverage.RemoveAll(x => x.StartsWith(module + ":"));
            _lastReport.ModuleCoverage.Add(module + ": " + DateTime.Now.ToString("s") + " / Quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()] + " / Pipeline=" + (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name ?? "Built-in") + " / Target=" + PlatformConfig.ActivePlatform + (module == IssueModule.CodeAnalysis ? " / " + Module1.CodeAnalysisRunner.LastCoverage : module == IssueModule.AssetAudit ? " / " + Module2.AssetAuditRunner.LastCoverage : module == IssueModule.SceneRendererAudit ? " / " + Module4.SceneMeshRendererScanner.LastCoverage : " / current effective pipeline and available settings; unknown fields explicitly excluded"));
            switch (module)
            {
                case IssueModule.CodeAnalysis:      _lastReport.CodeIssues = list; break;
                case IssueModule.AssetAudit:        _lastReport.AssetIssues = list; break;
                case IssueModule.URPDiagnostics:    _lastReport.URPIssues = list; break;
                case IssueModule.MobileWorkflow:    _lastReport.MobileIssues = list; break;
                case IssueModule.SceneRendererAudit:_lastReport.SceneRendererIssues = list; break;
            }
        }

        /// <summary>
        /// Kết thúc scan, kích hoạt event thông báo
        /// </summary>
        public static void FinalizeScan()
        {
            if (_lastReport == null) return;
            _lastReport.Completion = "Module results available; consult coverage and cancellation status";
            OnScanCompleted?.Invoke(_lastReport);
            Debug.Log($"[Optifunity] Scan hoàn tất: {_lastReport.TotalCount} issues " +
                      $"({_lastReport.ErrorCount} errors, {_lastReport.WarningCount} warnings) " +
                      $"— Rule score: {_lastReport.ScoreLabel}");
        }

        /// <summary>
        /// Xuất báo cáo dạng Markdown
        /// </summary>
        public static string ExportMarkdown(ScanReport report = null)
        {
            report ??= _lastReport;
            if (report == null) return "No scan data available.";

            var sb = new StringBuilder();
            sb.AppendLine("# Optifunity Performance Report");
            sb.AppendLine();
            sb.AppendLine($"**Scanned:** {report.ScanTime:yyyy-MM-dd HH:mm:ss}  ");
            sb.AppendLine($"**Project:** {report.ProjectName}  ");
            sb.AppendLine($"**Unity:** {report.UnityVersion}  ");
            sb.AppendLine($"**Scope:** {report.Scope}  ");
            sb.AppendLine($"**Module captures (unlisted modules not scanned):** {string.Join("; ", report.ModuleCoverage)}  ");
            sb.AppendLine($"**Completion:** {report.Completion}  ");
            sb.AppendLine($"**Quality / pipeline:** {report.Quality} / {report.Pipeline}  ");
            sb.AppendLine($"**Platform:** {report.BudgetSnapshot.Platform} " +
                          $"({report.BudgetSnapshot.PhysicalRamMB}MB RAM)  ");
            sb.AppendLine($"**Rule compliance score (scanned scope only; not measured performance):** {report.ScoreLabel}  ");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"## Summary");
            sb.AppendLine($"| Module | Errors | Warnings | Infos |");
            sb.AppendLine($"|--------|--------|----------|-------|");
            AppendModuleRow(sb, "Code Analysis",    report.CodeIssues);
            AppendModuleRow(sb, "Asset Audit",      report.AssetIssues);
            AppendModuleRow(sb, "Shader/Materials", report.URPIssues);
            AppendModuleRow(sb, "Render Pipeline",  report.MobileIssues);
            AppendModuleRow(sb, "Scene Renderer",   report.SceneRendererIssues);
            sb.AppendLine();

            AppendSection(sb, "## Code Analysis Issues",    report.CodeIssues);
            AppendSection(sb, "## Asset Audit Issues",      report.AssetIssues);
            AppendSection(sb, "## Shader/Materials Issues", report.URPIssues);
            AppendSection(sb, "## Render Pipeline Issues",  report.MobileIssues);
            AppendSection(sb, "## Scene Renderer Issues",   report.SceneRendererIssues);

            return sb.ToString();
        }

        /// <summary>
        /// Lưu báo cáo Markdown vào Assets/Optifunity/Reports/
        /// </summary>
        public static string SaveMarkdownReport(ScanReport report = null)
        {
            report ??= _lastReport;
            string content  = ExportMarkdown(report);
            string dir      = System.IO.Path.Combine(
                UnityEngine.Application.dataPath,
                "Optifunity", "Reports");
            System.IO.Directory.CreateDirectory(dir);
            string fileName = $"Report_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            string path     = System.IO.Path.Combine(dir, fileName);
            System.IO.File.WriteAllText(path, content, Encoding.UTF8);
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"[Optifunity] Báo cáo đã lưu tại: {path}");
            return path;
        }

        // ─── Private Helpers ──────────────────────────────────────────────────

        private static void AppendModuleRow(StringBuilder sb, string name, List<PerformanceIssue> issues)
        {
            int e = issues.Count(i => i.Severity == IssueSeverity.Error);
            int w = issues.Count(i => i.Severity == IssueSeverity.Warning);
            int n = issues.Count(i => i.Severity == IssueSeverity.Info);
            sb.AppendLine($"| {name} | {e} | {w} | {n} |");
        }

        private static void AppendSection(StringBuilder sb, string header, List<PerformanceIssue> issues)
        {
            if (issues.Count == 0) return;
            sb.AppendLine(header);
            sb.AppendLine();
            foreach (var issue in issues.OrderByDescending(i => i.Severity))
            {
                string icon = issue.Severity switch
                {
                    IssueSeverity.Error   => "🔴",
                    IssueSeverity.Warning => "🟡",
                    _                    => "🔵"
                };
                sb.AppendLine($"### {icon} {issue.Title}");
                sb.AppendLine($"- **Severity:** {issue.Severity}");
                sb.AppendLine($"- **Rule / evidence:** {issue.RuleId} / {issue.EvidenceLevel}");
                var rule = RuleCatalog.Get(issue.RuleId);
                sb.AppendLine($"- **Knowledge status:** {rule.verificationStatus}; {rule.applicability}");
                sb.AppendLine($"- **Sources:** {string.Join(", ", rule.sources ?? Array.Empty<string>())}");
                if (!string.IsNullOrEmpty(issue.AssetPath))
                    sb.AppendLine($"- **Asset:** `{issue.AssetPath}`");
                if (!string.IsNullOrEmpty(issue.CodeLocation))
                    sb.AppendLine($"- **Location:** `{issue.CodeLocation}`");
                sb.AppendLine($"- **Description:** {issue.Description}");
                sb.AppendLine($"- **Fix:** {issue.FixSuggestion}");
                sb.AppendLine();
            }
        }
    }
}
