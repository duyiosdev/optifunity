using System.Collections.Generic;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module4;

namespace Optifunity.Editor.Module7
{
    public static class MobileWorkflowAnalyzer
    {
        public static List<PerformanceIssue> Analyze(URPAssetScanner.URPSettingsSnapshot snapshot)
        {
            var issues = new List<PerformanceIssue>();

            if (!PlatformConfig.IsMobile)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Info,
                    Title = "Mobile workflow checks đang ở chế độ tham khảo",
                    Description = "Target Platform hiện không phải mobile, checklist chỉ mang tính tham khảo.",
                    FixSuggestion = "Chuyển Target Platform sang Android hoặc iOS để đánh giá chính xác hơn."
                });
                return issues;
            }

            if (!snapshot.IsURPActive)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Error,
                    Title = "URP chưa được kích hoạt cho mobile workflow",
                    Description = "Không tìm thấy URP Asset active nên không thể đánh giá workflow tối ưu mobile.",
                    FixSuggestion = "Gán URP Asset tại Project Settings > Graphics."
                });
                return issues;
            }

            AddStepIssue(issues, "Baseline", IssueSeverity.Info,
                "Thiết lập baseline thiết bị và ngân sách",
                $"Android tier: {PlatformConfig.GetAndroidTier()}, RAM mục tiêu: {PlatformConfig.AndroidPhysicalRamMB}MB, Texture budget khuyến nghị: {PlatformConfig.TextureBudgetMB:F0}MB.");

            AddStepIssue(issues, "URP Setup", snapshot.SRPBatcherEnabled ? IssueSeverity.Info : IssueSeverity.Error,
                "SRP Batcher", snapshot.SRPBatcherEnabled
                    ? "SRP Batcher đang bật."
                    : "SRP Batcher đang tắt, CPU render thread dễ bị nghẽn.");

            AddStepIssue(issues, "Lighting/Shadows", snapshot.ShadowCascadeCount <= 2 ? IssueSeverity.Info : IssueSeverity.Warning,
                "Shadow cascade count", $"Cascade hiện tại: {snapshot.ShadowCascadeCount}. Mobile khuyến nghị <= 2.");

            AddStepIssue(issues, "Lighting/Shadows", !snapshot.AdditionalLightShadowsEnabled ? IssueSeverity.Info : IssueSeverity.Warning,
                "Additional light shadows", snapshot.AdditionalLightShadowsEnabled
                    ? "Additional light shadows đang bật, có thể tốn GPU đáng kể."
                    : "Additional light shadows đang tắt.");

            AddStepIssue(issues, "Memory/Textures", !snapshot.HdrEnabled ? IssueSeverity.Info : IssueSeverity.Warning,
                "HDR", snapshot.HdrEnabled
                    ? "HDR đang bật trên mobile, tăng băng thông color buffer."
                    : "HDR đang tắt, phù hợp đa số mobile workloads.");

            AddStepIssue(issues, "Validation", snapshot.MsaaSampleCount <= 2 ? IssueSeverity.Info : IssueSeverity.Warning,
                "MSAA", $"MSAA hiện tại: x{snapshot.MsaaSampleCount}. Mobile thường phù hợp mức x2 hoặc thấp hơn.");

            return issues;
        }

        private static void AddStepIssue(List<PerformanceIssue> issues, string step, IssueSeverity severity, string title, string desc)
        {
            issues.Add(new PerformanceIssue
            {
                Severity = severity,
                Title = $"[{step}] {title}",
                Description = desc,
                FixSuggestion = "Mở tab Mobile để xem workflow và thứ tự tối ưu đề xuất."
            });
        }
    }
}
