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
        URPDiagnostics
    }

    /// <summary>
    /// Một vấn đề hiệu năng được phát hiện bởi plugin
    /// </summary>
    [Serializable]
    public class PerformanceIssue
    {
        public IssueModule    Module;
        public IssueSeverity  Severity;
        public string         Title;
        public string         Description;
        public string         AssetPath;       // Đường dẫn asset liên quan (nếu có)
        public string         CodeLocation;    // File:Line cho code issues
        public string         FixSuggestion;   // Hướng dẫn khắc phục
        public bool           CanAutoFix;      // Có thể tự động sửa không
        public string         AutoFixLabel;    // Nhãn nút Auto-Fix
        public System.Action  AutoFixAction;   // Hành động Auto-Fix (runtime)

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
        public BudgetSummary BudgetSnapshot;

        public List<PerformanceIssue> CodeIssues    = new();
        public List<PerformanceIssue> AssetIssues   = new();
        public List<PerformanceIssue> URPIssues     = new();

        public IEnumerable<PerformanceIssue> AllIssues =>
            CodeIssues.Concat(AssetIssues).Concat(URPIssues);

        public int ErrorCount   => AllIssues.Count(i => i.Severity == IssueSeverity.Error);
        public int WarningCount => AllIssues.Count(i => i.Severity == IssueSeverity.Warning);
        public int InfoCount    => AllIssues.Count(i => i.Severity == IssueSeverity.Info);
        public int TotalCount   => AllIssues.Count();

        /// <summary>
        /// Health Score 0-100 dựa trên số lượng và độ nghiêm trọng của issues
        /// </summary>
        public int HealthScore
        {
            get
            {
                if (TotalCount == 0) return 100;
                float penalty = ErrorCount * 10f + WarningCount * 3f + InfoCount * 0.5f;
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
                BudgetSnapshot = PlatformConfig.GetBudgetSummary()
            };
            return _lastReport;
        }

        /// <summary>
        /// Thêm issues từ một module vào báo cáo hiện tại
        /// </summary>
        public static void AddIssues(IssueModule module, IEnumerable<PerformanceIssue> issues)
        {
            if (_lastReport == null) BeginScan();

            var list = issues?.ToList() ?? new List<PerformanceIssue>();
            foreach (var issue in list) issue.Module = module;

            switch (module)
            {
                case IssueModule.CodeAnalysis:    _lastReport.CodeIssues   .AddRange(list); break;
                case IssueModule.AssetAudit:      _lastReport.AssetIssues  .AddRange(list); break;
                case IssueModule.URPDiagnostics:  _lastReport.URPIssues    .AddRange(list); break;
            }
        }

        /// <summary>
        /// Kết thúc scan, kích hoạt event thông báo
        /// </summary>
        public static void FinalizeScan()
        {
            OnScanCompleted?.Invoke(_lastReport);
            Debug.Log($"[Optifunity] Scan hoàn tất: {_lastReport.TotalCount} issues " +
                      $"({_lastReport.ErrorCount} errors, {_lastReport.WarningCount} warnings) " +
                      $"— Health Score: {_lastReport.HealthScore}/100");
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
            sb.AppendLine($"**Platform:** {report.BudgetSnapshot.Platform} " +
                          $"({report.BudgetSnapshot.PhysicalRamMB}MB RAM)  ");
            sb.AppendLine($"**Health Score:** {report.HealthScore}/100  ");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"## Summary");
            sb.AppendLine($"| Module | Errors | Warnings | Infos |");
            sb.AppendLine($"|--------|--------|----------|-------|");
            AppendModuleRow(sb, "Code Analysis",    report.CodeIssues);
            AppendModuleRow(sb, "Asset Audit",      report.AssetIssues);
            AppendModuleRow(sb, "URP Diagnostics",  report.URPIssues);
            sb.AppendLine();

            AppendSection(sb, "## Code Analysis Issues", report.CodeIssues);
            AppendSection(sb, "## Asset Audit Issues",   report.AssetIssues);
            AppendSection(sb, "## URP Issues",           report.URPIssues);

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
