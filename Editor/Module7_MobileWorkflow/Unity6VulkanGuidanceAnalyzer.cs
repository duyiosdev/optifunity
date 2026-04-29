using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module7
{
    public static class Unity6VulkanGuidanceAnalyzer
    {
        public static List<PerformanceIssue> Analyze()
        {
            var issues = new List<PerformanceIssue>();

            if (!PlatformConfig.IsMobile)
                return issues;

            if (PlatformConfig.ActivePlatform == Runtime.TargetPlatformType.Android)
            {
                var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
                bool hasVulkan = false;
                bool hasGles3 = false;

                foreach (var api in apis)
                {
                    if (api == GraphicsDeviceType.Vulkan) hasVulkan = true;
                    if (api == GraphicsDeviceType.OpenGLES3) hasGles3 = true;
                }

                issues.Add(new PerformanceIssue
                {
                    Severity = hasVulkan ? IssueSeverity.Info : IssueSeverity.Warning,
                    Title = "Unity 6 GRD path: Vulkan readiness",
                    Description = hasVulkan
                        ? "Vulkan đã bật cho Android, có thể benchmark theo hướng Unity 6 GRD."
                        : "Vulkan chưa bật cho Android nên thiếu baseline benchmark cho Unity 6 GRD workflow.",
                    FixSuggestion = "Benchmark A/B giữa Vulkan và OpenGLES3 trên các thiết bị tier thấp/trung/cao trước khi chốt API mặc định."
                });

                issues.Add(new PerformanceIssue
                {
                    Severity = hasGles3 ? IssueSeverity.Info : IssueSeverity.Warning,
                    Title = "Android GPU diversity fallback",
                    Description = hasGles3
                        ? "OpenGLES3 fallback đang có, phù hợp chiến lược phủ thiết bị rộng."
                        : "Thiếu OpenGLES3 fallback, có thể rủi ro trên thiết bị Vulkan driver kém ổn định.",
                    FixSuggestion = "Giữ fallback OpenGLES3 nếu mục tiêu là stability trên dải thiết bị Android rộng."
                });
            }

            if (PlatformConfig.ActivePlatform == Runtime.TargetPlatformType.iOS)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Info,
                    Title = "Unity 6 mobile guidance cho iOS",
                    Description = "iOS sử dụng Metal; workflow tối ưu nên tập trung shader variant stripping, overdraw, post-processing và bandwidth.",
                    FixSuggestion = "Ưu tiên profile bằng Xcode GPU tools và so khớp workload với checklist URP mobile trong tab này."
                });
            }

            return issues;
        }
    }
}
