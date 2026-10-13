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
        public static string LastCoverage { get; private set; } = "Not scanned";
        public static List<PerformanceIssue> Audit(bool myScriptsOnly = false)
        {
            var issues = new List<PerformanceIssue>();
            int inspected = 0, skipped = 0;
            LastCoverage = "Incomplete (scan in progress or failed)";
            var platform = PlatformConfig.ActivePlatform;
            int recommendedMaxSize = PlatformConfig.GetRecommendedMaxTextureSize();
            bool isMobile = PlatformConfig.IsMobile;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/")) continue;
                if (myScriptsOnly && !UI.DashboardWindow.IsUserCodePath(path)) { skipped++; continue; }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) { skipped++; continue; }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) { skipped++; continue; }

                int width  = texture.width;
                int height = texture.height;

                inspected++;
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
                                        $"NPOT support and padding depend on format, import settings and target GPU; measure actual memory.",
                        AssetPath     = path,
                        FixSuggestion = $"Chỉ resize nếu format hoặc mục đích sử dụng yêu cầu POT. " +
                                        $"Đối chiếu format được import trên thiết bị trước khi thay đổi."
                    });
                }

                // ─── 3. MaxSize quá lớn cho Mobile ────────────────────────────────
                if (isMobile)
                {
                    int maxSize = EffectiveMaxSize(importer);
                    if (maxSize > recommendedMaxSize)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity       = IssueSeverity.Warning,
                            Title          = $"Texture MaxSize ({maxSize}px) vượt ngưỡng khuyến nghị cho Mobile",
                            Description    = $"Plugin preset gợi ý MaxSize ≤ {recommendedMaxSize}px; đây không phải giới hạn thiết bị. " +
                                             $"Texture {System.IO.Path.GetFileName(path)} hiện là {maxSize}px.",
                            AssetPath      = path,
                            FixSuggestion  = $"Giảm MaxSize về {recommendedMaxSize}px trong TextureImporter → Platform Settings.",
                            CanAutoFix     = true,
                            AutoFixLabel   = $"Set MaxSize = {recommendedMaxSize}",

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
                        FixSuggestion = "Bật 'Generate Mip Maps' trong TextureImporter. Mip chain đầy đủ thường thêm khoảng 1/3 texel storage; lợi ích phụ thuộc cách sample và khoảng cách.",
                        CanAutoFix    = true,
                        AutoFixLabel  = "Enable MipMaps",

                    });
                }

                // ─── 5. Uncompressed texture ──────────────────────────────────────
                if (importer.textureCompression == TextureImporterCompression.Uncompressed)
                {
                    long estimatedVRAM = (long)width * height * 4; // ~4 bytes/pixel uncompressed
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Info,
                        Title         = "Texture không nén (Uncompressed) — tiêu tốn VRAM cực lớn",
                        Description   = $"Texture {width}x{height} uncompressed ≈ {estimatedVRAM / 1048576f:F1}MB VRAM. " +
                                        $"This base-level estimate excludes mipmaps, alignment and retained CPU copies.",
                        AssetPath     = path,
                        FixSuggestion = GetCompressionFix(platform)
                    });
                }

                // Unload texture khỏi memory ngay sau khi dùng
                Resources.UnloadAsset(texture);
            }

            LastCoverage = $"Texture2D: inspected={inspected}, skipped={skipped}; top-level imported assets under Assets only; package/generated/sub-asset coverage excluded";
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
                    Title         = $"Review texture compression format cho {platform}",
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
                    "iOS từ chip A8 (2014) hỗ trợ ASTC. Chọn format theo mục đích texture và device support, không bắt buộc ASTC cho mọi texture. " +
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

        private static int EffectiveMaxSize(TextureImporter importer)
        {
            string target = PlatformConfig.ActivePlatform == Runtime.TargetPlatformType.Android ? "Android" :
                PlatformConfig.ActivePlatform == Runtime.TargetPlatformType.iOS ? "iPhone" : "Standalone";
            var settings = importer.GetPlatformTextureSettings(target);
            return settings.overridden ? settings.maxTextureSize : importer.maxTextureSize;
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
