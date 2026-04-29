using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module4;

namespace Optifunity.Editor.Module7
{
    public static class MobileWorkflowGraphBuilder
    {
        public static MobileWorkflowGraphData BuildTraditional(URPAssetScanner.URPSettingsSnapshot snapshot)
        {
            var graph = new MobileWorkflowGraphData
            {
                Variant = WorkflowVariant.Traditional,
                GeneratedAt = System.DateTime.Now
            };

            bool staticBatching = IsStaticBatchingEnabled();

            bool urpOn = snapshot.IsURPActive;
            graph.Nodes.Add(MakeNode("urp", "URP", urpOn ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileNodeEvidenceLevel.URPAsset, urpOn ? "On" : "Off", urpOn ? "On" : "Off", "On", "Dùng URP Asset thống nhất cho toàn pipeline.", IssueSeverity.Error, "Project Settings > Graphics"));

            bool srpOn = snapshot.SRPBatcherEnabled;
            graph.Nodes.Add(MakeNode("srp_batcher", "SRP Batcher", srpOn ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileNodeEvidenceLevel.URPAsset, srpOn ? "On" : "Off", srpOn ? "On" : "Off", "On", "Bật SRP Batcher để giảm CPU render overhead.", IssueSeverity.Error, "URP Asset > Advanced"));

            graph.Nodes.Add(MakeNode("static_batching", "Static Batching", staticBatching ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileNodeEvidenceLevel.ProjectSetting, staticBatching ? "On" : "Off", staticBatching ? "On" : "Off", "On", "Traditional flow phù hợp khi static geometry lớn và ít thay đổi.", IssueSeverity.Warning, "Project Settings > Player > Other Settings"));

            graph.Nodes.Add(MakeNode("gpu_instancing", "GPU Instancing", MobileNodeStatus.Advisory,
                MobileNodeEvidenceLevel.AdvisoryOnly, "Per-material", "Per-material / per-shader", "Enable where repeated meshes/materials", "Bật Enable Instancing ở material lặp lại nhiều và shader hỗ trợ instancing.", IssueSeverity.Info, "Material Inspector / Shader"));

            bool dynOn = snapshot.DynamicBatchingEnabled;
            graph.Nodes.Add(MakeNode("dynamic_batching", "Dynamic Batching", MobileNodeStatus.Advisory,
                MobileNodeEvidenceLevel.URPAsset, dynOn ? "On" : "Off", dynOn ? "On" : "Off", "Context dependent", "Chỉ bật khi profile thực tế cho thấy lợi ích, đặc biệt khi SRP Batcher chưa cover tốt.", IssueSeverity.Info, "URP Asset > Rendering"));

            LinkLinear(graph, new[] { "urp", "srp_batcher", "static_batching", "gpu_instancing", "dynamic_batching" });
            return graph;
        }

        public static MobileWorkflowGraphData BuildUnity6Grd(URPAssetScanner.URPSettingsSnapshot snapshot)
        {
            var graph = new MobileWorkflowGraphData
            {
                Variant = WorkflowVariant.Unity6GRD,
                GeneratedAt = System.DateTime.Now
            };

            bool staticBatching = IsStaticBatchingEnabled();
            bool hasVulkan = false;
            bool hasMetal = PlatformConfig.ActivePlatform == Runtime.TargetPlatformType.iOS;

            if (PlatformConfig.ActivePlatform == Runtime.TargetPlatformType.Android)
            {
                var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
                foreach (var api in apis)
                {
                    if (api == GraphicsDeviceType.Vulkan)
                    {
                        hasVulkan = true;
                        break;
                    }
                }
            }

            bool apiMatched = hasVulkan || hasMetal;
            graph.Nodes.Add(MakeNode("graphics_api", "Graphic API (Vulkan/Metal)", apiMatched ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileNodeEvidenceLevel.ProjectSetting,
                hasMetal ? "Metal" : (hasVulkan ? "Vulkan" : "Neither Vulkan nor Metal"),
                hasMetal ? "Metal" : (hasVulkan ? "Vulkan" : "Not matched"),
                "Android: Vulkan, iOS: Metal",
                "Unity 6 GRD nên benchmark theo API mục tiêu và giữ fallback strategy phù hợp.",
                IssueSeverity.Warning,
                "Project Settings > Player > Graphics APIs"));

            bool shaderStripKnown = snapshot.ShaderStripEnabledFlag.HasValue;
            bool brgKnown = snapshot.BrgKeepAllVariantsFlag.HasValue;
            bool shaderStripOn = snapshot.ShaderStripEnabledFlag ?? false;
            bool brgKeepAll = snapshot.BrgKeepAllVariantsFlag ?? false;
            bool shaderStripMatched = shaderStripOn && brgKeepAll;
            MobileNodeStatus shaderStripStatus = (shaderStripKnown && brgKnown)
                ? (shaderStripMatched ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched)
                : MobileNodeStatus.Unknown;

            graph.Nodes.Add(MakeNode("shader_strip", "Shader Strip + BRG Variants", shaderStripStatus,
                (shaderStripKnown && brgKnown) ? MobileNodeEvidenceLevel.URPAsset : MobileNodeEvidenceLevel.AdvisoryOnly,
                (shaderStripKnown || brgKnown) ? $"Strip:{(shaderStripOn ? "On" : "Off")}, BRG KeepAll:{(brgKeepAll ? "On" : "Off")}" : "Unknown",
                (shaderStripKnown || brgKnown) ? $"Strip:{(shaderStripOn ? "On" : "Off")}, BRG KeepAll:{(brgKeepAll ? "On" : "Off")}" : "Not fully introspected",
                "Strip: On, BRG KeepAll: On",
                "Giữ đủ variants cho BRG/instancing path để tránh thiếu shader khi chạy thật.",
                IssueSeverity.Info,
                "Project Settings > Graphics / URP shader settings"));

            bool grdKnown = snapshot.GpuResidentDrawerEnabledFlag.HasValue && snapshot.GpuOcclusionCullingEnabledFlag.HasValue;
            bool grdResidentOn = snapshot.GpuResidentDrawerEnabledFlag ?? false;
            bool grdOccOn = snapshot.GpuOcclusionCullingEnabledFlag ?? false;
            bool grdMatched = grdResidentOn && grdOccOn;
            MobileNodeStatus grdStatus = grdKnown
                ? (grdMatched ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched)
                : MobileNodeStatus.Unknown;

            graph.Nodes.Add(MakeNode("grd_enable", "GRD Enable", grdStatus,
                grdKnown ? MobileNodeEvidenceLevel.URPAsset : MobileNodeEvidenceLevel.AdvisoryOnly,
                grdKnown ? $"Resident:{(grdResidentOn ? "On" : "Off")}, Occlusion:{(grdOccOn ? "On" : "Off")}" : "Unknown",
                grdKnown ? $"Resident:{(grdResidentOn ? "On" : "Off")}, Occlusion:{(grdOccOn ? "On" : "Off")}" : "Check Unity 6 URP asset fields",
                "Resident: On, Occlusion: On",
                "Nếu project dùng GRD path, bật đúng các toggle GRD trong URP/Renderer asset.",
                IssueSeverity.Warning,
                "URP Asset / Renderer Asset"));

            bool srpOnGrd = snapshot.SRPBatcherEnabled;
            graph.Nodes.Add(MakeNode("srp_batching_on", "SRP Batching (On)", srpOnGrd ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileNodeEvidenceLevel.URPAsset,
                srpOnGrd ? "On" : "Off",
                srpOnGrd ? "On" : "Off",
                "On",
                "SRP Batcher cần bật để đồng bộ GRD path với CPU render efficiency.",
                IssueSeverity.Error,
                "URP Asset > Advanced"));

            bool staticOffMatched = !staticBatching;
            graph.Nodes.Add(MakeNode("static_batching_off", "Static Batching (Off)", staticOffMatched ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileNodeEvidenceLevel.ProjectSetting,
                staticBatching ? "On" : "Off",
                staticBatching ? "On" : "Off",
                "Off",
                "GRD path thường ưu tiên instanced drawing, nên tắt static batching để tránh conflict workflow.",
                IssueSeverity.Warning,
                "Project Settings > Player > Other Settings"));

            graph.Nodes.Add(MakeNode("mesh_lod", "Generate Mesh LODs (Import)", MobileNodeStatus.Advisory,
                MobileNodeEvidenceLevel.AssetHeuristic,
                "Unknown",
                "Asset-level setting",
                "Enable for heavy meshes where LOD savings are measurable",
                "Kiểm tra import setting của các model chính để đảm bảo LOD strategy phù hợp budget mobile.",
                IssueSeverity.Info,
                "Model Importer"));

            graph.Nodes.Add(MakeNode("shader_material_support", "Shader/Material support GRD", MobileNodeStatus.Advisory,
                MobileNodeEvidenceLevel.AdvisoryOnly,
                "Unknown",
                "Project-wide auto verification unavailable",
                "Use GRD-compatible shaders/material settings",
                "Ưu tiên shader/material đã verify với instanced/BRG workflow để tránh fallback tốn CPU.",
                IssueSeverity.Warning,
                "Shader + Material assets"));

            bool dynOffMatched = !snapshot.DynamicBatchingEnabled;
            graph.Nodes.Add(MakeNode("dynamic_batching_off", "Dynamic Batching (Off)", dynOffMatched ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileNodeEvidenceLevel.URPAsset,
                snapshot.DynamicBatchingEnabled ? "On" : "Off",
                snapshot.DynamicBatchingEnabled ? "On" : "Off",
                "Off",
                "Trong GRD path, tắt dynamic batching để tránh chồng cơ chế với instanced drawing pipeline.",
                IssueSeverity.Warning,
                "URP Asset > Rendering"));

            LinkLinear(graph, new[]
            {
                "graphics_api",
                "shader_strip",
                "grd_enable",
                "srp_batching_on",
                "static_batching_off",
                "mesh_lod",
                "shader_material_support",
                "dynamic_batching_off"
            });

            return graph;
        }

        private static MobileWorkflowNode MakeNode(
            string id, string title, MobileNodeStatus status, MobileNodeEvidenceLevel evidence,
            string actualState, string current, string recommended, string suggestion, IssueSeverity mismatchSeverity, string source)
        {
            return new MobileWorkflowNode
            {
                Id = id,
                Title = title,
                Status = status,
                EvidenceLevel = evidence,
                ActualState = actualState,
                CurrentValue = current,
                RecommendedValue = recommended,
                Suggestion = suggestion,
                SeverityWhenMismatch = mismatchSeverity,
                SourcePathHint = source
            };
        }

        private static void LinkLinear(MobileWorkflowGraphData graph, IReadOnlyList<string> ids)
        {
            for (int i = 0; i < ids.Count - 1; i++)
            {
                graph.Edges.Add(new MobileWorkflowEdge
                {
                    FromId = ids[i],
                    ToId = ids[i + 1],
                    IsPrimaryPath = true
                });
            }
        }

        private static bool IsStaticBatchingEnabled()
        {
            BuildTargetGroup group = PlatformConfig.ActivePlatform switch
            {
                Runtime.TargetPlatformType.Android => BuildTargetGroup.Android,
                Runtime.TargetPlatformType.iOS => BuildTargetGroup.iOS,
                _ => BuildTargetGroup.Standalone
            };

            var methods = typeof(PlayerSettings).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            foreach (var method in methods)
            {
                if (method.Name != "GetBatchingForPlatform") continue;
                var ps = method.GetParameters();
                if (ps.Length != 3) continue;
                if (!ps[1].IsOut || !ps[2].IsOut) continue;

                object[] args = { group, 0, 0 };
                method.Invoke(null, args);
                int staticBatch = (int)args[1];
                return staticBatch != 0;
            }

            return false;
        }
    }
}
