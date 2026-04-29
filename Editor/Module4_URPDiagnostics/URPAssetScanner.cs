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

            // Unity 6 / GRD-related serialized fields (best-effort)
            TryReadBool(pipelineAsset, "m_GPUResidentDrawer", out snapshot.GpuResidentDrawerEnabledFlag);
            TryReadBool(pipelineAsset, "m_GPUOcclusionCulling", out snapshot.GpuOcclusionCullingEnabledFlag);
            TryReadBool(pipelineAsset, "m_ShaderStripping", out snapshot.ShaderStripEnabledFlag);
            TryReadBool(pipelineAsset, "m_BRGKeepAllVariants", out snapshot.BrgKeepAllVariantsFlag);

            return snapshot;
        }

        private static void TryReadBool(UnityEngine.Object target, string propName, out bool? value)
        {
            value = null;
            try
            {
                using var so = new SerializedObject(target);
                var prop = so.FindProperty(propName);
                if (prop != null && prop.propertyType == SerializedPropertyType.Boolean)
                    value = prop.boolValue;
            }
            catch
            {
                value = null;
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
