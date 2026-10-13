using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module4
{
    /// <summary>
    /// Đọc và phân tích cấu hình UniversalRenderPipelineAsset hiện tại.
    /// Kiểm tra tất cả properties liên quan đến hiệu năng và trả về dữ liệu raw.
    /// </summary>
    public static class URPAssetScanner
    {
        [Serializable]
        public class URPSettingsSnapshot
        {
            public bool  IsURPActive;
            public string AssetPath;

            // Quality Settings
            public bool  HdrEnabled;
            public int   MsaaSampleCount;
            public bool  MainLightShadowsEnabled;
            public int   MainLightShadowResolution;
            public bool  AdditionalLightShadowsEnabled;
            public int   AdditionalLightShadowResolution;
            public float ShadowDistance;
            public int   ShadowCascadeCount;
            public bool  SoftShadowsEnabled;

            // Rendering Features
            public bool  OpaqueTextureEnabled;
            public bool  DepthTextureEnabled;
            public bool  DepthPrimingEnabled;    // Depth Priming Mode

            // Pipeline Renderer
            public bool  SRPBatcherEnabled;
            public bool  DynamicBatchingEnabled;
            public bool  GpuResidentDrawerEnabled;

            // Lighting
            public int   AdditionalLightCount;
            public bool  MixedLightingSupported;

            // Post Processing
            public bool  PostProcessingEnabled;

            // Unity 6 / GRD-related (best-effort direct read)
            public bool? GpuResidentDrawerEnabledFlag;
            public bool? GrdRendererSupported;
            public string GrdSupportReason;
            public bool? GpuOcclusionCullingEnabledFlag;
            public bool? ShaderStripEnabledFlag;
            public bool? BrgKeepAllVariantsFlag;

            // Project / Player evidence for render pipeline workflows
            public string DefaultRenderPipelinePath;
            public string QualityRenderPipelinePath;
            public bool GraphicsAndQualityPipelineMatch;
            public bool StaticBatchingEnabled;
            public bool? StaticBatchingEnabledFlag;
            public string GraphicsApiSummary;
        }

        /// <summary>
        /// Đọc settings từ URP Asset hiện tại
        /// </summary>
        public static URPSettingsSnapshot Scan()
        {
            var snapshot = new URPSettingsSnapshot();

            var pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipelineAsset == null)
            {
                snapshot.IsURPActive = false;
                Debug.LogWarning("[Optifunity] Không tìm thấy Universal Render Pipeline Asset. " +
                                 "Đảm bảo project đang dùng URP (Project Settings → Graphics → Scriptable Render Pipeline Settings).");
                return snapshot;
            }

            snapshot.IsURPActive  = true;
            snapshot.AssetPath    = AssetDatabase.GetAssetPath(pipelineAsset);
            snapshot.DefaultRenderPipelinePath = AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline);
            snapshot.QualityRenderPipelinePath = AssetDatabase.GetAssetPath(QualitySettings.renderPipeline);
            snapshot.GraphicsAndQualityPipelineMatch = QualitySettings.renderPipeline == null || GraphicsSettings.defaultRenderPipeline == QualitySettings.renderPipeline;
            snapshot.StaticBatchingEnabledFlag = ReadStaticBatchingEnabled();
            snapshot.StaticBatchingEnabled = snapshot.StaticBatchingEnabledFlag ?? false;
            snapshot.GraphicsApiSummary = BuildGraphicsApiSummary();

            // ─── Quality ──────────────────────────────────────────────────────
            snapshot.HdrEnabled                     = pipelineAsset.supportsHDR;
            snapshot.MsaaSampleCount                = pipelineAsset.msaaSampleCount;
            snapshot.MainLightShadowsEnabled        = pipelineAsset.supportsMainLightShadows;
            snapshot.MainLightShadowResolution      = (int)pipelineAsset.mainLightShadowmapResolution;
            snapshot.AdditionalLightShadowsEnabled  = pipelineAsset.supportsAdditionalLightShadows;
            snapshot.AdditionalLightShadowResolution = (int)pipelineAsset.additionalLightsShadowmapResolution;
            snapshot.ShadowDistance                 = pipelineAsset.shadowDistance;
            snapshot.ShadowCascadeCount             = pipelineAsset.shadowCascadeCount;
            snapshot.SoftShadowsEnabled             = pipelineAsset.supportsSoftShadows;

            // ─── Rendering ────────────────────────────────────────────────────
            snapshot.OpaqueTextureEnabled  = pipelineAsset.supportsCameraOpaqueTexture;
            snapshot.DepthTextureEnabled   = pipelineAsset.supportsCameraDepthTexture;
            snapshot.DynamicBatchingEnabled = pipelineAsset.supportsDynamicBatching;

            // ─── Pipeline ─────────────────────────────────────────────────────
            snapshot.SRPBatcherEnabled = pipelineAsset.useSRPBatcher;

            // Additional lights count
            snapshot.AdditionalLightCount = pipelineAsset.maxAdditionalLightsCount;

            snapshot.GpuResidentDrawerEnabledFlag = pipelineAsset.gpuResidentDrawerMode != GPUResidentDrawerMode.Disabled;
            snapshot.GpuOcclusionCullingEnabledFlag = pipelineAsset.gpuResidentDrawerEnableOcclusionCullingInCameras;
            snapshot.GrdRendererSupported = pipelineAsset.IsGPUResidentDrawerSupportedBySRP(out string reason, out _);
            snapshot.GrdSupportReason = reason;
            // Stripping/BRG settings belong to global settings; absence of an adapter is explicitly unknown.
            snapshot.ShaderStripEnabledFlag = null;
            snapshot.BrgKeepAllVariantsFlag = null;

            return snapshot;
        }

        private static bool? ReadStaticBatchingEnabled()
        {
            BuildTargetGroup group = PlatformConfig.ActivePlatform switch
            {
                Runtime.TargetPlatformType.Android => BuildTargetGroup.Android,
                Runtime.TargetPlatformType.iOS => BuildTargetGroup.iOS,
                _ => BuildTargetGroup.Standalone
            };

            var methods = typeof(PlayerSettings).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            foreach (var method in methods)
            {
                if (method.Name != "GetBatchingForPlatform") continue;
                var ps = method.GetParameters();
                if (ps.Length != 3 || !ps[1].IsOut || !ps[2].IsOut) continue;

                object target;
                if (ps[0].ParameterType == typeof(BuildTargetGroup)) target = group;
                else if (ps[0].ParameterType == typeof(BuildTarget)) target = group == BuildTargetGroup.Android ? BuildTarget.Android :
                    group == BuildTargetGroup.iOS ? BuildTarget.iOS : EditorUserBuildSettings.activeBuildTarget;
                else continue;
                object[] args = { target, 0, 0 };
                try { method.Invoke(null, args); return (int)args[1] != 0; }
                catch (System.Reflection.TargetInvocationException) { return null; }
            }

            return null;
        }

        private static string BuildGraphicsApiSummary()
        {
            BuildTarget target = PlatformConfig.ActivePlatform switch
            {
                Runtime.TargetPlatformType.Android => BuildTarget.Android,
                Runtime.TargetPlatformType.iOS => BuildTarget.iOS,
                _ => EditorUserBuildSettings.activeBuildTarget
            };

            try
            {
                var apis = PlayerSettings.GetGraphicsAPIs(target);
                if (apis == null || apis.Length == 0) return "Auto / Default";
                var names = System.Array.ConvertAll(apis, api => api.ToString());
                return string.Join(", ", names);
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Tìm đường dẫn URP Asset hiện tại để có thể click-to-open từ UI
        /// </summary>
        public static void PingURPAsset()
        {
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }
    }
}
