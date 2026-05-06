using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.Rendering;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module4;

namespace Optifunity.Editor.Module7
{
    public static class MobileWorkflowGraphBuilder
    {
        public static MobileRenderPipelineReport BuildPipelineReport(URPAssetScanner.URPSettingsSnapshot snapshot)
        {
            var report = new MobileRenderPipelineReport { GeneratedAt = System.DateTime.Now };

            report.Pipelines.Add(BuildGrd(snapshot));
            report.Pipelines.Add(BuildSrpBatcher(snapshot));
            report.Pipelines.Add(BuildGpuInstancing(snapshot));
            report.Pipelines.Add(BuildStaticBatching(snapshot));
            report.Pipelines.Add(BuildDynamicBatching(snapshot));
            report.Pipelines.Add(BuildFallback(snapshot));

            foreach (var pipeline in report.Pipelines)
                pipeline.Status = AggregateStatus(pipeline.Checks);

            return report;
        }

        public static MobileWorkflowGraphData BuildTraditional(URPAssetScanner.URPSettingsSnapshot snapshot)
        {
            return LegacyGraphFromReport(BuildPipelineReport(snapshot), WorkflowVariant.Traditional);
        }

        public static MobileWorkflowGraphData BuildUnity6Grd(URPAssetScanner.URPSettingsSnapshot snapshot)
        {
            return LegacyGraphFromReport(BuildPipelineReport(snapshot), WorkflowVariant.Unity6GRD);
        }

        private static MobileRenderPipelineSystem BuildGrd(URPAssetScanner.URPSettingsSnapshot s)
        {
            bool apiReady = PlatformConfig.ActivePlatform == Runtime.TargetPlatformType.iOS
                ? ContainsApi(s.GraphicsApiSummary, "Metal")
                : ContainsApi(s.GraphicsApiSummary, "Vulkan");

            bool grdKnown = s.GpuResidentDrawerEnabledFlag.HasValue;
            bool occKnown = s.GpuOcclusionCullingEnabledFlag.HasValue;
            bool stripKnown = s.ShaderStripEnabledFlag.HasValue;
            bool brgKnown = s.BrgKeepAllVariantsFlag.HasValue;

            var p = NewPipeline("grd", "GPU Resident Drawer (GRD)", "GPU-driven rendering bằng BatchRendererGroup",
                "Giảm draw calls rất mạnh và đẩy instance data/culling sang GPU.",
                "Instance Data Buffer (GPU) → DrawMeshInstancedIndirect / BatchRendererGroup → rất ít draw calls.",
                "Scene lớn có nhiều instance lặp lại như cây, đá, props, foliage.",
                "Khuyến nghị không dùng Static Batching cùng GRD; MPB/shader không tương thích có thể làm fallback.",
                "Ưu tiên cho Unity 6.x mobile scenes lớn.", IssueSeverity.Error);

            p.Checks.Add(Check("grd_urp", "URP Asset Active", s.IsURPActive ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.GraphicsSettings, s.IsURPActive ? s.AssetPath : "None", "UniversalRenderPipelineAsset assigned",
                "GRD trong workflow này cần project chạy URP.", "Project Settings > Graphics", "graphics", IssueSeverity.Error));
            p.Checks.Add(Check("grd_api", "Mobile Graphics API", apiReady ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.PlayerSettings, s.GraphicsApiSummary, "Android: Vulkan / iOS: Metal",
                "API phù hợp giúp benchmark đúng GPU-driven path.", "Project Settings > Player > Graphics APIs", "player", IssueSeverity.Warning));
            p.Checks.Add(Check("grd_toggle", "GPU Resident Drawer", grdKnown ? BoolStatus(s.GpuResidentDrawerEnabledFlag.Value) : MobileNodeStatus.Unknown,
                MobileSettingSourceKind.URPAsset, grdKnown ? OnOff(s.GpuResidentDrawerEnabledFlag.Value) : "Unknown", "On",
                "Toggle GRD đọc best-effort từ serialized URP asset.", "URP Asset / Renderer settings", "urp", IssueSeverity.Error));
            p.Checks.Add(Check("grd_occlusion", "GPU Occlusion Culling", occKnown ? BoolStatus(s.GpuOcclusionCullingEnabledFlag.Value) : MobileNodeStatus.Unknown,
                MobileSettingSourceKind.URPAsset, occKnown ? OnOff(s.GpuOcclusionCullingEnabledFlag.Value) : "Unknown", "On",
                "GPU culling là lợi thế chính của GRD cho scene lớn.", "URP Asset / Renderer settings", "urp", IssueSeverity.Warning));
            p.Checks.Add(Check("grd_brg", "Shader Strip + BRG Variants", (stripKnown && brgKnown) ? BoolStatus(s.ShaderStripEnabledFlag.Value && s.BrgKeepAllVariantsFlag.Value) : MobileNodeStatus.Unknown,
                MobileSettingSourceKind.URPAsset,
                (stripKnown || brgKnown) ? $"Strip:{OnOff(s.ShaderStripEnabledFlag ?? false)}, BRG KeepAll:{OnOff(s.BrgKeepAllVariantsFlag ?? false)}" : "Unknown",
                "Strip: On, BRG KeepAll: On", "Giữ variants cần thiết để tránh fallback shader/runtime.", "URP Asset serialized shader settings", "urp", IssueSeverity.Warning));
            p.Checks.Add(Check("grd_static_off", "Static Batching Policy", !s.StaticBatchingEnabled ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.PlayerSettings, OnOff(s.StaticBatchingEnabled), "Off",
                "Tài liệu khuyến nghị tắt Static Batching khi dùng GRD để tránh xung đột chiến lược batching.", "Project Settings > Player > Static Batching", "player", IssueSeverity.Warning));
            p.Checks.Add(Check("grd_dynamic_off", "Dynamic Batching Policy", !s.DynamicBatchingEnabled ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.URPAsset, OnOff(s.DynamicBatchingEnabled), "Off",
                "GRD không cần legacy CPU dynamic batching.", "URP Asset > Rendering > Dynamic Batching", "urp", IssueSeverity.Warning));
            p.Checks.Add(Check("grd_mpb", "MaterialPropertyBlock / Shader Compatibility", MobileNodeStatus.Advisory,
                MobileSettingSourceKind.SceneAudit, "Asset-level", "No incompatible MPB/shader usage",
                "GRD không hoạt động nếu renderer dùng MPB hoặc shader không phù hợp; xác minh bằng Module 8/Frame Debugger.", "Scene Renderer Audit / Frame Debugger", "scene", IssueSeverity.Info));

            return p;
        }

        private static MobileRenderPipelineSystem BuildSrpBatcher(URPAssetScanner.URPSettingsSnapshot s)
        {
            var p = NewPipeline("srp", "SRP Batcher", "CPU optimization, không giảm draw call",
                "Giảm chi phí SetPass/state changes bằng cách giữ material data trên GPU.",
                "Draw Call 1, 2, N → SRP Batcher giảm CPU render overhead.",
                "Hầu hết URP mobile projects, đặc biệt scene nhiều renderer/material không instance được.",
                "Thường không tương thích với GPU Instancing trên cùng renderer/material path.",
                "Luôn bật trong URP trừ khi có lý do profiling cụ thể.", IssueSeverity.Error);

            p.Checks.Add(Check("srp_urp", "URP Asset Active", s.IsURPActive ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.GraphicsSettings, s.IsURPActive ? s.AssetPath : "None", "UniversalRenderPipelineAsset assigned", "SRP Batcher là URP/SRP path.", "Project Settings > Graphics", "graphics", IssueSeverity.Error));
            p.Checks.Add(Check("srp_toggle", "SRP Batcher", s.SRPBatcherEnabled ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.URPAsset, OnOff(s.SRPBatcherEnabled), "On", "Giảm CPU cost cho draw setup.", "URP Asset > Advanced > SRP Batcher", "urp", IssueSeverity.Error));
            p.Checks.Add(Check("srp_quality", "Quality Override Detected", s.GraphicsAndQualityPipelineMatch ? MobileNodeStatus.Matched : MobileNodeStatus.Partial,
                MobileSettingSourceKind.QualitySettings, $"Graphics:{s.DefaultRenderPipelinePath} | Quality:{s.QualityRenderPipelinePath}", "Intentional override or same active target URP Asset", "Quality level đang override Render Pipeline Asset. Đây không nhất thiết là lỗi; cần đảm bảo analyzer đang đọc đúng URP asset của quality target.", "Project Settings > Graphics / Quality", "quality", IssueSeverity.Warning));
            p.Checks.Add(Check("srp_shader", "Shader SRP Batcher Compatibility", MobileNodeStatus.Advisory,
                MobileSettingSourceKind.MaterialAsset, "Shader-level", "SRP Batcher compatible CBUFFER layout", "Xác minh trong shader/material audit hoặc Frame Debugger.", "Shader / Material assets", "material", IssueSeverity.Info));
            return p;
        }

        private static MobileRenderPipelineSystem BuildGpuInstancing(URPAssetScanner.URPSettingsSnapshot s)
        {
            var p = NewPipeline("instancing", "GPU Instancing (Traditional)", "Giảm draw calls bằng DrawMeshInstanced",
                "Batch các renderer cùng Mesh + Material, tối đa khoảng 1023 instances mỗi batch.",
                "Instance 1..N → DrawMeshInstanced → ít draw calls hơn.",
                "Nhóm object lặp lại vừa phải khi không dùng GRD.",
                "Thường không chạy song song hiệu quả với SRP Batcher cho cùng path; MPB có giới hạn.",
                "Dùng có chọn lọc cho repeated Mesh+Material.", IssueSeverity.Warning);

            p.Checks.Add(Check("gi_urp", "URP Asset Active", s.IsURPActive ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.GraphicsSettings, s.IsURPActive ? s.AssetPath : "None", "UniversalRenderPipelineAsset assigned", "Cần URP active để checklist mobile nhất quán.", "Project Settings > Graphics", "graphics", IssueSeverity.Error));
            p.Checks.Add(Check("gi_material", "Material Enable GPU Instancing", MobileNodeStatus.Advisory,
                MobileSettingSourceKind.MaterialAsset, "Per-material", "Enable on repeated Mesh+Material", "Không có global project setting; cần kiểm tra material lặp trong scene.", "Material Inspector > Enable GPU Instancing", "material", IssueSeverity.Info));
            p.Checks.Add(Check("gi_srp", "SRP Batcher Compatibility Awareness", s.SRPBatcherEnabled ? MobileNodeStatus.Partial : MobileNodeStatus.Matched,
                MobileSettingSourceKind.URPAsset, s.SRPBatcherEnabled ? "SRP Batcher On" : "SRP Batcher Off", "Choose per workload", "Tài liệu lưu ý SRP Batcher và GPU Instancing thường không tương thích trong nhiều trường hợp.", "URP Asset > SRP Batcher", "urp", IssueSeverity.Info));
            p.Checks.Add(Check("gi_mpb", "Per-instance Data / MPB Policy", MobileNodeStatus.Advisory,
                MobileSettingSourceKind.SceneAudit, "Renderer-level", "Only compatible per-instance data", "MPB có thể phá batching/GRD tùy shader và field.", "Scene Renderer Audit", "scene", IssueSeverity.Info));
            return p;
        }

        private static MobileRenderPipelineSystem BuildStaticBatching(URPAssetScanner.URPSettingsSnapshot s)
        {
            var p = NewPipeline("static", "Static Batching", "Build-time mesh combine",
                "Gộp mesh static theo material để giảm draw calls, đổi lại tăng memory.",
                "Combined Mesh per Material → Draw Call.",
                "Environment static thật sự: tường, nhà, đường, props không di chuyển.",
                "Không khuyến nghị dùng cùng GRD; object thay đổi không phù hợp.",
                "Bật khi scene static và memory budget cho phép.", IssueSeverity.Warning);

            p.Checks.Add(Check("sb_toggle", "Player Static Batching", s.StaticBatchingEnabled ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.PlayerSettings, OnOff(s.StaticBatchingEnabled), "On for static environment path", "Player setting quyết định static batching theo platform.", "Project Settings > Player > Static Batching", "player", IssueSeverity.Warning));
            p.Checks.Add(Check("sb_memory", "Memory Overhead", MobileNodeStatus.Advisory,
                MobileSettingSourceKind.Advisory, "Scene-dependent", "Profile mesh memory", "Static batching copy mesh data nên có thể tăng memory trên mobile.", "Memory Profiler / Build Report", "advisory", IssueSeverity.Info));
            p.Checks.Add(Check("sb_static_flags", "Renderer Static Flags", MobileNodeStatus.Advisory,
                MobileSettingSourceKind.SceneAudit, "Renderer-level", "Static objects only", "Chỉ object không đổi transform/material mới phù hợp.", "Scene Renderer Audit", "scene", IssueSeverity.Info));
            return p;
        }

        private static MobileRenderPipelineSystem BuildDynamicBatching(URPAssetScanner.URPSettingsSnapshot s)
        {
            var p = NewPipeline("dynamic", "Dynamic Batching (Legacy)", "Runtime CPU mesh combine",
                "Gộp mesh nhỏ lúc render nhưng tốn CPU, thường ít lợi trên mobile hiện đại.",
                "CPU Combine Runtime → Draw Call.",
                "Chỉ cân nhắc cho mesh rất nhỏ khi profiling xác nhận có lợi.",
                "Không phù hợp với SRP Batcher/GPU Instancing/GRD trong nhiều trường hợp.",
                "Mặc định nên tắt, bật chỉ khi có số liệu profiling.", IssueSeverity.Warning);

            p.Checks.Add(Check("db_toggle", "URP Dynamic Batching", !s.DynamicBatchingEnabled ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.URPAsset, OnOff(s.DynamicBatchingEnabled), "Off by default", "Legacy CPU path; tránh bật mặc định trên mobile.", "URP Asset > Dynamic Batching", "urp", IssueSeverity.Warning));
            p.Checks.Add(Check("db_mesh_limit", "Small Mesh Limit", MobileNodeStatus.Advisory,
                MobileSettingSourceKind.SceneAudit, "Scene-dependent", "~300 verts/mesh or less", "Dynamic batching chỉ áp dụng cho mesh nhỏ và có nhiều giới hạn.", "Scene Renderer Audit", "scene", IssueSeverity.Info));
            return p;
        }

        private static MobileRenderPipelineSystem BuildFallback(URPAssetScanner.URPSettingsSnapshot s)
        {
            var p = NewPipeline("fallback", "Fallback (Normal Draw)", "Không tối ưu: mỗi renderer = draw call",
                "Renderer không thỏa điều kiện các hệ thống tối ưu sẽ đi normal draw.",
                "Draw Call 1, 2, N → CPU + GPU cost cao.",
                "Chỉ là trạng thái fallback cần giảm thiểu, không phải mục tiêu cấu hình.",
                "Số draw calls thực tế phụ thuộc material/pass/lightmap/keywords/camera/culling/LOD.",
                "Tránh bằng cách dùng đúng pipeline phù hợp và xác minh bằng Frame Debugger.", IssueSeverity.Info);

            p.Checks.Add(Check("fd_urp", "URP Active For Diagnostics", s.IsURPActive ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched,
                MobileSettingSourceKind.GraphicsSettings, s.IsURPActive ? s.AssetPath : "None", "URP active", "Nếu URP inactive, các tối ưu URP không được đánh giá đúng.", "Project Settings > Graphics", "graphics", IssueSeverity.Error));
            p.Checks.Add(Check("fd_frame_debugger", "Frame Debugger Verification", MobileNodeStatus.Advisory,
                MobileSettingSourceKind.FrameDebugger, "Manual", "Look for DrawMeshInstancedIndirect / SRP Batcher / DrawMeshInstanced", "Frame Debugger là nguồn xác nhận path thực tế theo tài liệu.", "Window > Analysis > Frame Debugger", "frame_debugger", IssueSeverity.Info));
            p.Checks.Add(Check("fd_scene", "Scene Renderer Audit", MobileNodeStatus.Advisory,
                MobileSettingSourceKind.SceneAudit, "Manual", "Audit MPB / instancing / static / LOD", "Dùng Module 8 để tìm renderer dễ rơi vào fallback.", "Optifunity > Scene Renderer Audit", "scene", IssueSeverity.Info));
            return p;
        }

        private static MobileRenderPipelineSystem NewPipeline(string id, string title, string subtitle, string goal, string flow,
            string useCase, string compatibility, string recommendation, IssueSeverity severity)
        {
            return new MobileRenderPipelineSystem
            {
                Id = id,
                Title = title,
                Subtitle = subtitle,
                Goal = goal,
                RenderFlow = flow,
                BestUseCase = useCase,
                CompatibilityNotes = compatibility,
                Recommendation = recommendation,
                SeverityWhenMismatch = severity
            };
        }

        private static MobilePipelineSettingCheck Check(string id, string title, MobileNodeStatus status, MobileSettingSourceKind source,
            string actual, string recommended, string description, string locateHint, string locateKey, IssueSeverity severity)
        {
            return new MobilePipelineSettingCheck
            {
                Id = id,
                Title = title,
                Status = status,
                SourceKind = source,
                ActualValue = string.IsNullOrEmpty(actual) ? "None" : actual,
                RecommendedValue = recommended,
                Description = description,
                LocateHint = locateHint,
                LocateKey = locateKey,
                SeverityWhenMismatch = severity
            };
        }

        private static MobileNodeStatus AggregateStatus(IReadOnlyList<MobilePipelineSettingCheck> checks)
        {
            if (checks == null || checks.Count == 0) return MobileNodeStatus.Unknown;
            if (checks.Any(c => c.Status == MobileNodeStatus.Mismatched)) return MobileNodeStatus.Mismatched;
            if (checks.Any(c => c.Status == MobileNodeStatus.Partial)) return MobileNodeStatus.Partial;
            if (checks.Any(c => c.Status == MobileNodeStatus.Unknown)) return MobileNodeStatus.Unknown;
            if (checks.All(c => c.Status == MobileNodeStatus.Advisory)) return MobileNodeStatus.Advisory;
            return MobileNodeStatus.Matched;
        }

        private static MobileNodeStatus BoolStatus(bool value) => value ? MobileNodeStatus.Matched : MobileNodeStatus.Mismatched;
        private static string OnOff(bool value) => value ? "On" : "Off";
        private static bool ContainsApi(string summary, string api) => !string.IsNullOrEmpty(summary) && summary.Contains(api);

        private static MobileWorkflowGraphData LegacyGraphFromReport(MobileRenderPipelineReport report, WorkflowVariant variant)
        {
            var graph = new MobileWorkflowGraphData { Variant = variant, GeneratedAt = report.GeneratedAt };
            foreach (var p in report.Pipelines)
            {
                graph.Nodes.Add(new MobileWorkflowNode
                {
                    Id = p.Id,
                    Title = p.Title,
                    Subtitle = p.Subtitle,
                    Status = p.Status,
                    EvidenceLevel = MobileNodeEvidenceLevel.ProjectSetting,
                    ActualState = p.Goal,
                    CurrentValue = p.Status.ToString(),
                    RecommendedValue = p.Recommendation,
                    Suggestion = p.CompatibilityNotes,
                    SeverityWhenMismatch = p.SeverityWhenMismatch,
                    SourcePathHint = p.Checks.FirstOrDefault()?.LocateHint
                });
            }
            return graph;
        }
    }
}
