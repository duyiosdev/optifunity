using System;
using System.Collections.Generic;
using UnityEditor;

namespace Optifunity.Editor.Module5
{
    /// <summary>
    /// Orchestrate: FrameDataReader → BottleneckClassifier → SpikeAnalysisReport.
    /// Cache kết quả phân tích theo frameIndex để không đọc lại.
    /// [Force Re-import: Triggered at 2026-04-20 17:26]
    /// </summary>
    public static class SpikeAnalysisRunner
    {
        // Cache: frameIndex → analysisReport
        private static readonly Dictionary<int, SpikeAnalysisReport> _cache
            = new(capacity: 100);

        // Cache: frameIndex → rawFrameSnapshot (needed for detailed Memory/Rendering tabs)
        private static readonly Dictionary<int, FrameSnapshot> _snapshotCache
            = new(capacity: 100);

        private const int MAX_CACHE_SIZE = 200;

        // Kết quả phân tích gần nhất
        public static SpikeAnalysisReport LastAnalysis { get; private set; }

        /// <summary>
        /// Phân tích frame. Trả về từ cache nếu đã phân tích trước đó.
        /// </summary>
        public static SpikeAnalysisReport AnalyzeFrame(int frameIndex, bool forceRefresh = false)
        {
            if (frameIndex < 0) return CreateInvalidReport(frameIndex, "Frame index âm.");

            // Cache hit
            if (!forceRefresh && _cache.TryGetValue(frameIndex, out var cached))
            {
                LastAnalysis = cached;
                return cached;
            }

            // ─── Đọc frame data ───────────────────────────────────────────────
            FrameSnapshot snapshot;
            try
            {
                snapshot = FrameDataReader.ReadFrame(frameIndex);
            }
            catch (Exception ex)
            {
                return CreateInvalidReport(frameIndex,
                    $"Exception khi đọc frame: {ex.Message}\n{ex.StackTrace}");
            }

            // ─── Phân loại ────────────────────────────────────────────────────
            float avgMs = SpikeDetector.RollingAverage;
            SpikeAnalysisReport report;
            try
            {
                report = BottleneckClassifier.Classify(snapshot, avgMs);
            }
            catch (Exception ex)
            {
                // Snapshot hợp lệ nhưng classifier lỗi — vẫn trả về report cơ bản
                report = new SpikeAnalysisReport
                {
                    FrameIndex       = frameIndex,
                    FrameTotalMs     = snapshot.TotalCpuTimeMs,
                    AverageFrameMs   = avgMs,
                    IsSpike          = snapshot.TotalCpuTimeMs > avgMs * 1.5f,
                    PrimaryBottleneck = BottleneckType.Unknown,
                    PrimaryTitle      = "Lỗi phân loại",
                    PrimaryDescription = ex.Message,
                    TopSlowSamples    = snapshot.GetTopByTime(10)
                };
            }

            // ─── Cache và return ──────────────────────────────────────────────
            EvictCacheIfFull();
            _cache[frameIndex] = report;
            _snapshotCache[frameIndex] = snapshot;
            LastAnalysis = report;
            return report;
        }

        /// <summary>
        /// Lấy raw snapshot từ cache. Trả về null nếu chưa được phân tích.
        /// </summary>
        public static FrameSnapshot GetCachedSnapshot(int frameIndex)
        {
            if (_snapshotCache.TryGetValue(frameIndex, out var snapshot))
                return snapshot;
            return null;
        }

        /// <summary>
        /// Phân tích nhiều frames cùng lúc (ví dụ toàn bộ spike range).
        /// </summary>
        public static List<SpikeAnalysisReport> AnalyzeRange(int firstFrame, int lastFrame)
        {
            var results = new List<SpikeAnalysisReport>();
            for (int f = firstFrame; f <= lastFrame && f - firstFrame < 60; f++)
                results.Add(AnalyzeFrame(f));
            return results;
        }

        /// <summary>Clear cache (ví dụ khi Profiler bị Clear)</summary>
        public static void ClearCache()
        {
            _cache.Clear();
            _snapshotCache.Clear();
            LastAnalysis = null;
        }

        private static void EvictCacheIfFull()
        {
            if (_cache.Count >= MAX_CACHE_SIZE)
            {
                // Xóa 20% cache cũ nhất (simple eviction)
                var keys = new List<int>(_cache.Keys);
                keys.Sort();
                int removeCount = MAX_CACHE_SIZE / 5;
                for (int i = 0; i < removeCount && i < keys.Count; i++)
                {
                    _cache.Remove(keys[i]);
                    _snapshotCache.Remove(keys[i]);
                }
            }
        }

        private static SpikeAnalysisReport CreateInvalidReport(int frameIndex, string message)
        {
            return new SpikeAnalysisReport
            {
                FrameIndex        = frameIndex,
                PrimaryBottleneck = BottleneckType.Unknown,
                PrimaryTitle      = "Không thể phân tích",
                PrimaryDescription = message
            };
        }
    }
}
