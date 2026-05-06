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
                    Severity = IssueSeverity.Info,
                    Title = "[Guidance] Unity 6 GRD benchmark strategy",
                    Description = hasVulkan
                        ? "Vulkan đã bật; có thể benchmark A/B giữa Vulkan và OpenGLES3 theo từng device tier."
                        : "Vulkan chưa bật; khó benchmark đầy đủ Unity 6 GRD path trên Android.",
                    FixSuggestion = "Đo GPU frame time và stability trên tier thấp/trung/cao trước khi chốt API mặc định."
                });

                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Info,
                    Title = "[Guidance] Android API fallback strategy",
                    Description = hasGles3
                        ? "OpenGLES3 fallback đang có; phù hợp cho chiến lược phủ thiết bị rộng."
                        : "Thiếu OpenGLES3 fallback; cân nhắc rủi ro thiết bị Vulkan driver kém ổn định.",
                    FixSuggestion = "Quyết định fallback theo dữ liệu crash/perf từ test farm và thiết bị thật."
                });
            }

            if (PlatformConfig.ActivePlatform == Runtime.TargetPlatformType.iOS)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Info,
                    Title = "[Guidance] iOS Metal optimization focus",
                    Description = "iOS dùng Metal; checklist nên ưu tiên shader variant control, overdraw, post-processing và bandwidth.",
                    FixSuggestion = "Profile bằng Xcode GPU tools và đối chiếu với checklist node trong tab Render Pipeline."
                });
            }

            return issues;
        }
    }
}
