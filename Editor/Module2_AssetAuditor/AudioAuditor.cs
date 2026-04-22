using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module2
{
    /// <summary>
    /// Kiểm toán AudioClip: Load Type, Compression Format, duration threshold.
    /// Phát hiện AudioClip dài bị Decompress on Load — tiêu tốn hàng chục MB RAM.
    /// </summary>
    public static class AudioAuditor
    {
        public static List<PerformanceIssue> Audit(bool myScriptsOnly = false)
        {
            var issues = new List<PerformanceIssue>();
            float durationThreshold = PlatformConfig.AudioStreamingThresholdSeconds;

            string[] guids = AssetDatabase.FindAssets("t:AudioClip");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/")) continue;
                if (myScriptsOnly && !UI.DashboardWindow.IsUserCodePath(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                float  duration   = clip.length;
                string clipName   = System.IO.Path.GetFileName(path);
                var    sampleSettings = importer.defaultSampleSettings;

                // ─── 1. Decompress on Load cho file dài ──────────────────────
                if (duration > durationThreshold &&
                    sampleSettings.loadType == AudioClipLoadType.DecompressOnLoad)
                {
                    // Ước tính bộ nhớ PCM raw (stereo 44100Hz float32)
                    float estimatedRawMB = duration * 44100f * 2f * 4f / 1_048_576f;

                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Error,
                        Title         = $"AudioClip '{clipName}' ({duration:F1}s): Decompress on Load tiêu tốn ~{estimatedRawMB:F0}MB RAM",
                        Description   = $"AudioClip dài {duration:F1}s với LoadType = DecompressOnLoad sẽ giải nén " +
                                        $"toàn bộ file vào RAM ngay khi scene load (~{estimatedRawMB:F0}MB PCM raw). " +
                                        $"Một file MP3 5MB phình thành 40-50MB bộ nhớ.",
                        AssetPath     = path,
                        FixSuggestion = "Đổi Load Type = Streaming trong AudioImporter → Default nếu là nhạc nền/ambient dài. " +
                                        "Streaming chỉ giữ ~200KB buffer, chi phí CPU decoding không đáng kể.",
                        CanAutoFix    = true,
                        AutoFixLabel  = "Set LoadType = Streaming",
                        AutoFixAction = () => AutoFixStreaming(path)
                    });
                }

                // ─── 2. AudioClip ngắn nhưng dùng Streaming (không hiệu quả) ──
                if (duration < 1f && sampleSettings.loadType == AudioClipLoadType.Streaming)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = $"AudioClip ngắn '{clipName}' ({duration:F2}s) không nên dùng Streaming",
                        Description   = "Streaming tối ưu cho file dài (>5s). File ngắn như Sound FX nên dùng " +
                                        "Decompress on Load hoặc Compressed In Memory để tránh latency khi play.",
                        AssetPath     = path,
                        FixSuggestion = "Đổi Load Type = Compressed In Memory (hoặc Decompress on Load nếu file rất ngắn <0.5s).",
                        CanAutoFix    = true,
                        AutoFixLabel  = "Set LoadType = CompressedInMemory",
                        AutoFixAction = () => AutoFixCompressedInMemory(path)
                    });
                }

                // ─── 3. Kiểm tra Compression Format ──────────────────────────
                if (sampleSettings.compressionFormat == AudioCompressionFormat.PCM && duration > 1f)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = $"AudioClip '{clipName}' dùng PCM (không nén) — lãng phí bộ nhớ",
                        Description   = "PCM không nén tốn rất nhiều bộ nhớ. Trên mobile nên dùng Vorbis (iOS/Android).",
                        AssetPath     = path,
                        FixSuggestion = "Đổi Compression Format = Vorbis với Quality ~70% — giảm 8-10x dung lượng mà ít mất chất lượng.",
                        CanAutoFix    = true,
                        AutoFixLabel  = "Set Compression = Vorbis 70%",
                        AutoFixAction = () => AutoFixVorbis(path)
                    });
                }

                // ─── 4. Force Mono cho Sound FX trên Mobile ──────────────────
                if (PlatformConfig.IsMobile && !importer.forceToMono && duration < 5f)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Info,
                        Title         = $"AudioClip '{clipName}': xem xét Force To Mono trên Mobile",
                        Description   = "Sound effects ngắn thường không cần stereo. Force To Mono giảm 50% bộ nhớ cho audio clip.",
                        AssetPath     = path,
                        FixSuggestion = "Bật Force To Mono trong AudioImporter nếu clip là SFX không cần không gian âm thanh 3D.",
                        CanAutoFix    = true,
                        AutoFixLabel  = "Enable Force To Mono",
                        AutoFixAction = () => AutoFixForceMono(path)
                    });
                }

                Resources.UnloadAsset(clip);
            }

            return issues;
        }

        private static void AutoFixStreaming(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        private static void AutoFixCompressedInMemory(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        private static void AutoFixVorbis(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        private static void AutoFixForceMono(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            importer.forceToMono = true;
            importer.SaveAndReimport();
        }
    }
}
