using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module2
{
    /// <summary>
    /// Hook vào Unity Asset Pipeline qua AssetPostprocessor.
    /// Tự động kiểm tra và có thể tự động sửa settings khi asset mới được import.
    /// </summary>
    public class AssetPostprocessorHook : AssetPostprocessor
    {
        // ─── Texture Hook ─────────────────────────────────────────────────────
        void OnPreprocessTexture()
        {
            if (!ShouldProcess(assetPath)) return;

            var importer = assetImporter as TextureImporter;
            if (importer == null) return;

            bool isMobile = PlatformConfig.IsMobile;
            bool autoFix  = PlatformConfig.AutoFixEnabled;

            if (!autoFix)
            {
                // Chỉ log cảnh báo, không tự sửa
                Debug.Log($"[Optifunity] Texture mới import: {assetPath} — chạy Asset Audit để kiểm tra.");
                return;
            }

            // Auto-apply MipMap cho textures 3D (không phải UI/Sprite)
            bool isSprite = importer.textureType == TextureImporterType.Sprite;
            bool isUI = assetPath.Contains("/UI/") || assetPath.Contains("\\UI\\");

            if (!isSprite && !isUI && !importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                Debug.Log($"[Optifunity][AutoFix] Đã bật MipMap cho texture: {assetPath}");
            }

            // Auto-apply MaxSize cho Mobile
            if (isMobile)
            {
                int recommended = PlatformConfig.GetRecommendedMaxTextureSize();
                string platformName = PlatformConfig.ActivePlatform == Runtime.TargetPlatformType.Android
                    ? "Android" : "iPhone";

                var platformSettings = importer.GetPlatformTextureSettings(platformName);
                if (!platformSettings.overridden || platformSettings.maxTextureSize > recommended)
                {
                    platformSettings.overridden = true;
                    platformSettings.maxTextureSize = recommended;
                    importer.SetPlatformTextureSettings(platformSettings);
                    Debug.Log($"[Optifunity][AutoFix] Đã set MaxSize={recommended} cho {platformName}: {assetPath}");
                }
            }
        }

        // ─── Model/Mesh Hook ──────────────────────────────────────────────────
        void OnPreprocessModel()
        {
            if (!ShouldProcess(assetPath)) return;
            if (!PlatformConfig.AutoFixEnabled) return;

            var importer = assetImporter as ModelImporter;
            if (importer == null) return;

            // Auto-disable Read/Write
            if (importer.isReadable)
            {
                importer.isReadable = false;
                Debug.Log($"[Optifunity][AutoFix] Đã tắt Read/Write cho Mesh: {assetPath}");
            }

            // Auto-enable Animation Compression
            if (importer.importAnimation &&
                importer.animationCompression == ModelImporterAnimationCompression.Off)
            {
                importer.animationCompression = ModelImporterAnimationCompression.Optimal;
                Debug.Log($"[Optifunity][AutoFix] Đã bật Animation Compression cho: {assetPath}");
            }
        }

        // ─── Audio Hook ───────────────────────────────────────────────────────
        void OnPreprocessAudio()
        {
            if (!ShouldProcess(assetPath)) return;
            if (!PlatformConfig.AutoFixEnabled) return;

            var importer = assetImporter as AudioImporter;
            if (importer == null) return;

            var settings = importer.defaultSampleSettings;
            bool changed = false;

            // Đổi PCM sang Vorbis nếu chưa có compression
            if (settings.compressionFormat == AudioCompressionFormat.PCM)
            {
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                changed = true;
                Debug.Log($"[Optifunity][AutoFix] Đã đổi AudioClip sang Vorbis 70%: {assetPath}");
            }

            if (changed)
                importer.defaultSampleSettings = settings;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────
        private static bool ShouldProcess(string path)
        {
            // Không can thiệp vào Packages
            return !path.StartsWith("Packages/") &&
                   !path.Contains("\\Packages\\");
        }
    }
}
