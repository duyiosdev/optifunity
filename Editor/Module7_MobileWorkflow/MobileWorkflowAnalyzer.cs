using System.Collections.Generic;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module4;

namespace Optifunity.Editor.Module7
{
    public static class MobileWorkflowAnalyzer
    {
        public static List<PerformanceIssue> Analyze(URPAssetScanner.URPSettingsSnapshot snapshot, MobileRenderPipelineReport report = null)
        {
            var issues = new List<PerformanceIssue>();

            if (!PlatformConfig.IsMobile)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Info,
                    Title = "Render Pipeline checklist đang ở chế độ tham khảo",
                    Description = "Target Platform hiện không phải mobile, checklist chỉ mang tính tham khảo.",
                    FixSuggestion = "Chuyển Target Platform sang Android hoặc iOS để đánh giá chính xác hơn."
                });
            }

            if (report == null)
                report = MobileWorkflowRunner.LastPipelineReport ?? MobileWorkflowGraphBuilder.BuildPipelineReport(snapshot);

            if (report?.Pipelines == null || report.Pipelines.Count == 0)
                return issues;

            issues.Add(new PerformanceIssue
            {
                Severity = IssueSeverity.Info,
                Title = "[Pipeline Architecture] Unity renderer paths are parallel",
                Description = "Module 7 đang đánh giá GRD, SRP Batcher, GPU Instancing, Static Batching, Dynamic Batching và Fallback như các hệ thống tối ưu độc lập cho từng renderer.",
                FixSuggestion = "Mở tab Render Pipeline để xem match/mismatch theo từng pipeline và locate setting tương ứng."
            });

            foreach (var pipeline in report.Pipelines)
            {
                foreach (var check in pipeline.Checks)
                {
                    if (check.Status == MobileNodeStatus.Mismatched)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = check.SeverityWhenMismatch,
                            Title = $"[{pipeline.Title}] {check.Title} mismatched",
                            Description = $"Current: {check.ActualValue} | Recommended: {check.RecommendedValue}",
                            FixSuggestion = $"{check.Description} (Locate: {check.LocateHint})"
                        });
                    }
                    else if (check.Status == MobileNodeStatus.Unknown)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = IssueSeverity.Info,
                            Title = $"[{pipeline.Title}] {check.Title} unknown",
                            Description = "Không đọc được trạng thái setting này từ API/serialized fields ở version Unity hiện tại.",
                            FixSuggestion = $"Xác minh thủ công tại: {check.LocateHint}."
                        });
                    }
                    else if (check.Status == MobileNodeStatus.Partial || check.Status == MobileNodeStatus.Advisory)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = IssueSeverity.Info,
                            Title = $"[{pipeline.Title}] {check.Title}",
                            Description = check.Description,
                            FixSuggestion = $"Current: {check.ActualValue} | Recommended: {check.RecommendedValue} | Locate: {check.LocateHint}."
                        });
                    }
                }
            }

            return issues;
        }
    }
}
