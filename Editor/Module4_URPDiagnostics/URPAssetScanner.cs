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
            public bool? GpuOcclusionCullingEnabledFlag;
            public bool? ShaderStripEnabledFlag;
            public bool? BrgKeepAllVariantsFlag;

            // Project / Player evidence for render pipeline workflows
            public string DefaultRenderPipelinePath;
            public string QualityRenderPipelinePath;
            public bool GraphicsAndQualityPipelineMatch;
            public bool StaticBatchingEnabled;
            public string GraphicsApiSummary;
        }

        /// <summary>
        /// Đọc settings từ URP Asset hiện tại
        /// </summary>
        public static URPSettingsSnapshot Scan()
        {
            var snapshot = new URPSettingsSnapshot();

            var pipelineAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
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
            snapshot.GraphicsAndQualityPipelineMatch = GraphicsSettings.defaultRenderPipeline == QualitySettings.renderPipeline;
            snapshot.StaticBatchingEnabled = ReadStaticBatchingEnabled();
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
            // SRP Batcher — đọc qua SerializedObject để access private field
            try
            {
                using var so = new SerializedObject(pipelineAsset);
                var srpBatcherProp = so.FindProperty("m_UseSRPBatcher");
                snapshot.SRPBatcherEnabled = srpBatcherProp?.boolValue ?? false;
            }
            catch
            {
                snapshot.SRPBatcherEnabled = true; // assume enabled by default in URP
            }

            // Additional lights count
            snapshot.AdditionalLightCount = pipelineAsset.maxAdditionalLightsCount;

            // Unity 6 / GRD-related serialized fields (best-effort across URP versions)
            TryReadBoolAny(pipelineAsset, out snapshot.GpuResidentDrawerEnabledFlag,
                "m_GPUResidentDrawer", "m_GpuResidentDrawer", "m_GPUResidentDrawerMode",
                "m_UseGPUResidentDrawer", "m_EnableGPUResidentDrawer", "m_EnableGpuResidentDrawer");
            TryReadBoolAny(pipelineAsset, out snapshot.GpuOcclusionCullingEnabledFlag,
                "m_GPUOcclusionCulling", "m_GpuOcclusionCulling", "m_SupportsGPUOcclusionCulling",
                "m_EnableGPUOcclusionCulling", "m_EnableGpuOcclusionCulling");
            TryReadBoolAny(pipelineAsset, out snapshot.ShaderStripEnabledFlag,
                "m_ShaderStripping", "m_ShaderStrippingMode", "m_ShaderVariantLogLevel",
                "m_UseShaderVariantStripping", "m_EnableShaderStripping");
            TryReadBoolAny(pipelineAsset, out snapshot.BrgKeepAllVariantsFlag,
                "m_BRGKeepAllVariants", "m_BrgKeepAllVariants", "m_KeepAllBRGVariants",
                "m_KeepAllBatchRendererGroupVariants", "m_BatchRendererGroupKeepAllVariants");

            return snapshot;
        }

        private static void TryReadBoolAny(UnityEngine.Object target, out bool? value, params string[] propNames)
        {
            value = null;
            try
            {
                using var so = new SerializedObject(target);
                foreach (var propName in propNames)
                {
                    var prop = so.FindProperty(propName);
                    if (TryGetBoolLikeValue(prop, out bool boolValue))
                    {
                        value = boolValue;
                        return;
                    }
                }
            }
            catch
            {
                value = null;
            }
        }

        private static bool TryGetBoolLikeValue(SerializedProperty prop, out bool value)
        {
            value = false;
            if (prop == null) return false;

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    value = prop.boolValue;
                    return true;
                case SerializedPropertyType.Integer:
                    value = prop.intValue != 0;
                    return true;
                case SerializedPropertyType.Enum:
                    value = prop.enumValueIndex != 0;
                    return true;
                default:
                    return false;
            }
        }

        private static bool ReadStaticBatchingEnabled()
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
                if (ps.Length != 3 || !ps[1].IsOut || !ps[2].IsOut) continue;

                object[] args = { group, 0, 0 };
                method.Invoke(null, args);
                return (int)args[1] != 0;
            }

            return false;
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
            var asset = GraphicsSettings.defaultRenderPipeline;
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }
    }
}
