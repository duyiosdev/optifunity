using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module2
{
    /// <summary>
    /// Kiểm toán cấu hình Texture2D theo tiêu chuẩn nền tảng.
    /// Kiểm tra: Compression format, POT, MaxSize, MipMap.
    /// </summary>
    public static class TextureAuditor
    {
        public static List<PerformanceIssue> Audit()
        {
            var issues = new List<PerformanceIssue>();
            var platform = PlatformConfig.ActivePlatform;
            int recommendedMaxSize = PlatformConfig.GetRecommendedMaxTextureSize();
            bool isMobile = PlatformConfig.IsMobile;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/")) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) continue;

                int width  = texture.width;
                int height = texture.height;

                // ─── 1. Compression Format ────────────────────────────────────────
                CheckCompressionFormat(issues, importer, path, platform);

                // ─── 2. Power Of Two (POT) ────────────────────────────────────────
                if (isMobile && (!IsPowerOfTwo(width) || !IsPowerOfTwo(height)))
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = "Texture NPOT — không phải Power of Two",
                        Description   = $"Texture {width}x{height} không phải bội số lũy thừa 2. " +
                                        $"GPU di động không thể nén NPOT textures hiệu quả, dẫn đến VRAM tăng gấp đôi.",
                        AssetPath     = path,
                        FixSuggestion = $"Resize texture về kích thước POT gần nhất ({NextPOT(width)}x{NextPOT(height)}). " +
                                        $"Hoặc bật Non Power of Two = Pad trong TextureImporter."
                    });
                }

                // ─── 3. MaxSize quá lớn cho Mobile ────────────────────────────────
                if (isMobile)
                {
                    int maxSize = importer.maxTextureSize;
                    if (maxSize > recommendedMaxSize && (width > recommendedMaxSize || height > recommendedMaxSize))
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity       = IssueSeverity.Warning,
                            Title          = $"Texture MaxSize ({maxSize}px) vượt ngưỡng khuyến nghị cho Mobile",
                            Description    = $"Thiết bị tier {PlatformConfig.GetAndroidTier()} nên giới hạn MaxSize ≤ {recommendedMaxSize}px. " +
                                             $"Texture {System.IO.Path.GetFileName(path)} hiện là {maxSize}px.",
                            AssetPath      = path,
                            FixSuggestion  = $"Giảm MaxSize về {recommendedMaxSize}px trong TextureImporter → Platform Settings.",
                            CanAutoFix     = true,
                            AutoFixLabel   = $"Set MaxSize = {recommendedMaxSize}",
                            AutoFixAction  = () => AutoFixMaxSize(path, recommendedMaxSize)
                        });
                    }
                }

                // ─── 4. MipMap cho 3D textures ────────────────────────────────────
                bool isUI = path.Contains("/UI/") || path.Contains("\\UI\\") ||
                            importer.textureType == TextureImporterType.Sprite;

                if (!isUI && !importer.mipmapEnabled && (width > 64 || height > 64))
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = "Thiếu MipMap trên Texture 3D",
                        Description   = $"Texture '{System.IO.Path.GetFileName(path)}' không có Mipmap. " +
                                        $"Khi object ở xa camera, GPU phải sample texture full-res gây aliasing và cache miss.",
                        AssetPath     = path,
                        FixSuggestion = "Bật 'Generate Mip Maps' trong TextureImporter. Tăng dung lượng file 33% nhưng cải thiện cache hit và hiệu năng GPU đáng kể.",
                        CanAutoFix    = true,
                        AutoFixLabel  = "Enable MipMaps",
                        AutoFixAction = () => AutoFixMipmap(path)
                    });
                }

                // ─── 5. Uncompressed texture ──────────────────────────────────────
                if (importer.textureCompression == TextureImporterCompression.Uncompressed)
                {
                    long estimatedVRAM = (long)width * height * 4; // ~4 bytes/pixel uncompressed
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Error,
                        Title         = "Texture không nén (Uncompressed) — tiêu tốn VRAM cực lớn",
                        Description   = $"Texture {width}x{height} uncompressed ≈ {estimatedVRAM / 1048576f:F1}MB VRAM. " +
                                        $"Trên mobile 2GB RAM, một kết cấu 4K uncompressed chiếm ~1/3 ngân sách bộ nhớ.",
                        AssetPath     = path,
                        FixSuggestion = GetCompressionFix(platform)
                    });
                }

                // Unload texture khỏi memory ngay sau khi dùng
                Resources.UnloadAsset(texture);
            }

            return issues;
        }

        // ─── Compression Format Check ─────────────────────────────────────────────

        private static void CheckCompressionFormat(
            List<PerformanceIssue> issues, TextureImporter importer,
            string path, Runtime.TargetPlatformType platform)
        {
            // Lấy platform settings nếu có override
            TextureImporterPlatformSettings platformSettings = GetPlatformSettings(importer, platform);

            bool hasValidCompression = platformSettings?.overridden == true
                ? IsValidCompression(platformSettings.format, platform)
                : importer.textureCompression != TextureImporterCompression.Uncompressed;

            if (!hasValidCompression)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Error,
                    Title         = $"Texture thiếu định dạng nén tối ưu cho {platform}",
                    Description   = GetCompressionDescription(platform),
                    AssetPath     = path,
                    FixSuggestion = GetCompressionFix(platform)
                });
            }
        }

        private static TextureImporterPlatformSettings GetPlatformSettings(
            TextureImporter importer, Runtime.TargetPlatformType platform)
        {
            string platformName = platform switch
            {
                Runtime.TargetPlatformType.Android => "Android",
                Runtime.TargetPlatformType.iOS     => "iPhone",
                Runtime.TargetPlatformType.PC      => "Standalone",
                _ => "Standalone"
            };
            return importer.GetPlatformTextureSettings(platformName);
        }

        private static bool IsValidCompression(TextureImporterFormat format, Runtime.TargetPlatformType platform)
        {
            return platform switch
            {
                Runtime.TargetPlatformType.Android =>
                    format == TextureImporterFormat.ASTC_4x4  ||
                    format == TextureImporterFormat.ASTC_6x6  ||
                    format == TextureImporterFormat.ASTC_8x8  ||
                    format == TextureImporterFormat.ASTC_10x10||
                    format == TextureImporterFormat.ASTC_12x12||
                    format == TextureImporterFormat.ETC2_RGBA8||
                    format == TextureImporterFormat.ETC_RGB4,
                Runtime.TargetPlatformType.iOS =>
                    format == TextureImporterFormat.ASTC_4x4  ||
                    format == TextureImporterFormat.ASTC_6x6  ||
                    format == TextureImporterFormat.ASTC_8x8  ||
                    format == TextureImporterFormat.PVRTC_RGBA4,
                Runtime.TargetPlatformType.PC =>
                    format == TextureImporterFormat.BC7       ||
                    format == TextureImporterFormat.DXT5      ||
                    format == TextureImporterFormat.DXT1,
                _ => true
            };
        }

        private static string GetCompressionDescription(Runtime.TargetPlatformType platform) =>
            platform switch
            {
                Runtime.TargetPlatformType.Android =>
                    "Android cần dùng ASTC (hỗ trợ 80%+ thiết bị hiện đại) hoặc ETC2 (compatibility). " +
                    "ASTC 4x4 = 8bpp, ASTC 8x8 = 2bpp. Kết cấu uncompressed tốn gấp 4-16 lần VRAM.",
                Runtime.TargetPlatformType.iOS =>
                    "iOS từ chip A8 (2014) hỗ trợ ASTC. Bắt buộc dùng ASTC cho mọi kết cấu iOS hiện đại. " +
                    "iOS Jetsam sẽ kill app nếu VRAM vượt High-Water Mark mà không báo trước.",
                Runtime.TargetPlatformType.PC =>
                    "PC/DirectX cần dùng BC7 (DirectX 11+) hoặc DXT5. BC7 cung cấp chất lượng cao hơn DXT5 đáng kể.",
                _ => "Thiếu định dạng nén kết cấu phù hợp với nền tảng."
            };

        private static string GetCompressionFix(Runtime.TargetPlatformType platform) =>
            platform switch
            {
                Runtime.TargetPlatformType.Android =>
                    $"Vào TextureImporter → Android → Format: chọn ASTC 6x6 (hoặc {PlatformConfig.GetRecommendedASTCBlockSize()}). " +
                    $"MaxSize: {PlatformConfig.GetRecommendedMaxTextureSize()}px.",
                Runtime.TargetPlatformType.iOS =>
                    "Vào TextureImporter → iOS → Format: ASTC 6x6. Đảm bảo MaxSize ≤ 2048.",
                Runtime.TargetPlatformType.PC =>
                    "Vào TextureImporter → Standalone → Format: BC7. Phù hợp cho mọi texture có Alpha.",
                _ => "Cấu hình TextureImporter → Platform Settings → chọn định dạng nén phù hợp."
            };

        // ─── Auto Fix Helpers ─────────────────────────────────────────────────────

        private static void AutoFixMaxSize(string path, int maxSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.maxTextureSize = maxSize;
            importer.SaveAndReimport();
            Debug.Log($"[Optifunity] Auto-Fixed MaxSize={maxSize} cho: {path}");
        }

        private static void AutoFixMipmap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            Debug.Log($"[Optifunity] Auto-Fixed MipMap=true cho: {path}");
        }

        // ─── Math Helpers ─────────────────────────────────────────────────────────

        private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        private static int NextPOT(int n)
        {
            int pot = 1;
            while (pot < n) pot <<= 1;
            return pot;
        }
    }
}
