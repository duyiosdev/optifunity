using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module4;

namespace Optifunity.Editor.Module7
{
    public static class ProjectSettingsMismatchAnalyzer
    {
        public static List<PerformanceIssue> Analyze(URPAssetScanner.URPSettingsSnapshot snapshot)
        {
            var issues = new List<PerformanceIssue>();

            if (!snapshot.IsURPActive)
                return issues;

            var defaultRp = GraphicsSettings.defaultRenderPipeline;
            var qualityRp = QualitySettings.renderPipeline;

            if (defaultRp == null)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Error,
                    Title = "GraphicsSettings chưa gán Render Pipeline",
                    Description = "GraphicsSettings.defaultRenderPipeline đang null.",
                    FixSuggestion = "Gán URP Asset tại Project Settings > Graphics."
                });
                return issues;
            }

            if (defaultRp != qualityRp)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Warning,
                    Title = "Graphics/Quality Render Pipeline mismatch",
                    Description = "GraphicsSettings.defaultRenderPipeline và QualitySettings.renderPipeline đang khác nhau.",
                    FixSuggestion = "Đồng bộ Scriptable Render Pipeline Asset giữa Graphics và Quality để tránh hành vi không nhất quán."
                });
            }

            if (defaultRp is not UniversalRenderPipelineAsset)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Error,
                    Title = "Render Pipeline hiện tại không phải URP",
                    Description = "Asset pipeline active không phải UniversalRenderPipelineAsset.",
                    FixSuggestion = "Chuyển sang URP Asset để dùng đúng checklist Render Pipeline của Optifunity."
                });
            }

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

                if (!hasVulkan)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity = IssueSeverity.Info,
                        Title = "Android thiếu Vulkan trong Graphics APIs",
                        Description = "Vulkan chưa xuất hiện trong danh sách Graphics APIs cho Android.",
                        FixSuggestion = "Thêm Vulkan để benchmark đúng path Unity 6 GRD trên thiết bị mục tiêu."
                    });
                }

                if (!hasGles3)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Title = "Android thiếu OpenGLES3 fallback",
                        Description = "Danh sách Graphics APIs không có OpenGLES3 fallback.",
                        FixSuggestion = "Giữ OpenGLES3 fallback nếu project cần độ phủ thiết bị Android rộng."
                    });
                }
            }

            return issues;
        }
    }
}
