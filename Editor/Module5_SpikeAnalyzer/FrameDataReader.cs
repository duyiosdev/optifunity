using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

#if UNITY_2021_1_OR_NEWER
using UnityEditor.Profiling;
#endif

namespace Optifunity.Editor.Module5
{
    /// <summary>
    /// Đọc dữ liệu profiler của một frame cụ thể từ ProfilerDriver API.
    /// Xây dựng ProfilerSample tree và flat lookup dictionary.
    /// Yêu cầu: Profiler đang bật Deep Profile hoặc đã record.
    /// </summary>
    public static class FrameDataReader
    {
        // Độ sâu tối đa khi traverse hierarchy (tránh SO với scene phức tạp)
        private const int MAX_DEPTH = 10;

        // Stat names để lấy frame time tổng
        private const string STAT_CPU_MAIN_THREAD = "CPU Main Thread Frame Time";
        private const string STAT_GPU_FRAME_TIME  = "GPU Frame Time";
        private const string STAT_GC_ALLOC        = "GC.Alloc";

        /// <summary>
        /// Đọc toàn bộ dữ liệu của một frame.
        /// Trả về null nếu frame không hợp lệ hoặc profiler chưa record.
        /// </summary>
        public static FrameSnapshot ReadFrame(int frameIndex)
        {
            var snapshot = new FrameSnapshot { FrameIndex = frameIndex };

            // ─── Kiểm tra tính hợp lệ ────────────────────────────────────────
            if (frameIndex < 0)
            {
                snapshot.IsValid      = false;
                snapshot.ErrorMessage = "Frame index không hợp lệ (< 0).";
                return snapshot;
            }

            int first = ProfilerDriver.firstFrameIndex;
            int last  = ProfilerDriver.lastFrameIndex;

            if (first < 0 || last < 0 || frameIndex < first || frameIndex > last)
            {
                snapshot.IsValid      = false;
                snapshot.ErrorMessage = $"Frame #{frameIndex} không nằm trong dữ liệu profiler hiện tại " +
                                        $"(available: {first}..{last}). Đảm bảo Profiler đang record.";
                return snapshot;
            }

#if UNITY_2021_1_OR_NEWER
            // ─── Đọc qua HierarchyFrameDataView ───────────────────────────────
            HierarchyFrameDataView frameView = null;
            try
            {
                frameView = ProfilerDriver.GetHierarchyFrameDataView(
                    frameIndex,
                    0, // main thread
                    HierarchyFrameDataView.ViewModes.Default,
                    HierarchyFrameDataView.columnTotalTime,
                    false /* ascending = false → sort descending */);

                if (frameView == null || !frameView.valid)
                {
                    snapshot.IsValid      = false;
                    snapshot.ErrorMessage = $"HierarchyFrameDataView không hợp lệ cho frame #{frameIndex}. " +
                                            $"Thử record lại trong Profiler window.";
                    return snapshot;
                }

                snapshot.IsValid        = true;
                snapshot.TotalCpuTimeMs = frameView.frameTimeMs;

                // Traverse hierarchy từ root
                int rootId = frameView.GetRootItemID();
                var rootChildren = new List<int>();
                frameView.GetItemChildren(rootId, rootChildren);

                foreach (int childId in rootChildren)
                {
                    var sample = TraverseSample(frameView, childId, 0, snapshot.TotalCpuTimeMs);
                    snapshot.TopLevelSamples.Add(sample);
                    RegisterToLookup(snapshot.SamplesByName, sample);
                }

                // Tổng GC Alloc cho frame này
                snapshot.TotalGCAllocBytes = CalculateTotalGCAlloc(snapshot);

                // Tính toán Category Breakdown
                snapshot.CategoryBreakdown = GenerateCategoryBreakdown(snapshot);
            }
            catch (Exception ex)
            {
                snapshot.IsValid      = false;
                snapshot.ErrorMessage = $"Lỗi khi đọc frame data: {ex.Message}";
                return snapshot;
            }
            finally
            {
                frameView?.Dispose();
            }

            // ─── GPU time (từ stats) ──────────────────────────────────────────
            snapshot.TotalGpuTimeMs = ReadStatForFrame(STAT_GPU_FRAME_TIME, frameIndex);

#else
            // Unity < 2021.1 — dùng legacy API
            snapshot.IsValid        = true;
            snapshot.TotalCpuTimeMs = ReadStatForFrame(STAT_CPU_MAIN_THREAD, frameIndex);
            snapshot.TotalGpuTimeMs = ReadStatForFrame(STAT_GPU_FRAME_TIME,  frameIndex);
            snapshot.ErrorMessage   = "Unity < 2021.1: chỉ có frame time stats, không có sample hierarchy.";
#endif

            return snapshot;
        }

        /// <summary>
        /// Lấy frame time (ms) của nhiều frames liên tiếp để vẽ timeline.
        /// Hiệu quả vì dùng GetStatisticsValues thay vì đọc toàn bộ hierarchy.
        /// </summary>
        public static FrameTimeData[] ReadFrameTimeline(int firstFrame, int lastFrame)
        {
            if (firstFrame < 0 || lastFrame < firstFrame)
                return Array.Empty<FrameTimeData>();

            int count = lastFrame - firstFrame + 1;
            var result = new FrameTimeData[count];

            // ─── Primary: HierarchyFrameDataView.frameTimeMs ──────────────────
            // This is the reliable API for per-frame CPU time from a Profiler recording.
            // Only use for reasonable batch sizes to avoid hitching.
            bool anyData = false;
            int batchLimit = Mathf.Min(count, 300); // cap heavy per-frame reads
            for (int i = 0; i < batchLimit; i++)
            {
                int frameIndex = firstFrame + i;
                result[i] = new FrameTimeData { FrameIndex = frameIndex };

                try
                {
#if UNITY_2021_1_OR_NEWER
                    using var frameView = ProfilerDriver.GetHierarchyFrameDataView(
                        frameIndex, 0,
                        HierarchyFrameDataView.ViewModes.Default,
                        HierarchyFrameDataView.columnTotalTime, false);

                    if (frameView != null && frameView.valid && frameView.frameTimeMs > 0)
                    {
                        result[i].TotalMs = frameView.frameTimeMs;
                        anyData = true;
                    }
#endif
                }
                catch { /* frame not recorded or disposed */ }
            }
            // Fill any remaining entries not covered by batchLimit
            for (int i = batchLimit; i < count; i++)
                result[i] = new FrameTimeData { FrameIndex = firstFrame + i };

            // ─── Fallback: GetStatisticsValues with multiple candidate stat names ───
            if (!anyData)
            {
                string[] statCandidates = {
                    "CPU Main Thread Frame Time",
                    "CPU Total Frame Time",
                    "Main Thread",
                    "CPU"
                };

                foreach (var statName in statCandidates)
                {
                    int statId = ProfilerDriver.GetStatisticsIdentifier(statName);
                    if (statId < 0) continue;

                    var values = new float[count];
                    float maxVal;
                    ProfilerDriver.GetStatisticsValues(statId, firstFrame, 1.0f, values, out maxVal);

                    if (maxVal > 0)
                    {
                        for (int i = 0; i < count; i++)
                            result[i].TotalMs = values[i];
                        anyData = true;
                        break;
                    }
                }
            }

            MarkSpikes(result, 1.5f);
            return result;
        }

        // ─── Private: Traverse ────────────────────────────────────────────────

#if UNITY_2021_1_OR_NEWER
        private static ProfilerSample TraverseSample(
            HierarchyFrameDataView view, int itemId, int depth, float frameTotalMs)
        {
            string name      = view.GetItemName(itemId);
            float  totalTime = view.GetItemColumnDataAsSingle(itemId, HierarchyFrameDataView.columnTotalTime);
            float  selfTime  = view.GetItemColumnDataAsSingle(itemId, HierarchyFrameDataView.columnSelfTime);
            float  gcAlloc   = view.GetItemColumnDataAsSingle(itemId, HierarchyFrameDataView.columnGcMemory);
            float  calls     = view.GetItemColumnDataAsSingle(itemId, HierarchyFrameDataView.columnCalls);

            var sample = new ProfilerSample
            {
                Name           = name,
                TotalTimeMs    = totalTime,
                SelfTimeMs     = selfTime,
                GCAllocBytes   = (long)(gcAlloc),
                CallCount      = Mathf.RoundToInt(calls),
                PercentOfFrame = frameTotalMs > 0 ? totalTime / frameTotalMs * 100f : 0f,
                Depth          = depth,
                Category       = DetermineCategory(name)
            };

            // Chỉ traverse children nếu chưa quá sâu
            if (depth < MAX_DEPTH)
            {
                var children = new List<int>();
                view.GetItemChildren(itemId, children);
                foreach (int childId in children)
                {
                    sample.Children.Add(TraverseSample(view, childId, depth + 1, frameTotalMs));
                }
            }

            return sample;
        }
        private static ProfilerCategory DetermineCategory(string name)
        {
            if (string.IsNullOrEmpty(name)) return ProfilerCategory.Unknown;
            
            if (name.StartsWith("Physics.") || name.StartsWith("Physics2D.") || name.Contains("FixedUpdate"))
                return ProfilerCategory.Physics;
            if (name.StartsWith("Camera.Render") || name.StartsWith("Render.") || name.Contains("Gfx.") || name.Contains("Drawing"))
                return ProfilerCategory.Rendering;
            if (name.Contains("Update") || name.Contains("Coroutine") || name.Contains("ScriptRun"))
                return ProfilerCategory.ScriptUpdate;
            if (name.StartsWith("GC.") || name.Contains("GarbageCollect"))
                return ProfilerCategory.GarbageCollection;
            if (name.StartsWith("Animator.") || name.Contains("Animation"))
                return ProfilerCategory.Animation;
            if (name.StartsWith("Canvas.") || name.StartsWith("UI.") || name.StartsWith("UGUI"))
                return ProfilerCategory.UI;
            if (name.StartsWith("Audio.") || name.Contains("Sound"))
                return ProfilerCategory.Audio;
            if (name.StartsWith("Loading.") || name.Contains("AssetBundle") || name.Contains("Instantiate"))
                return ProfilerCategory.AssetLoading;
            if (name == "Profiler.EndFrame" || name.Contains("Overhead"))
                return ProfilerCategory.Overhead;

            return ProfilerCategory.Unknown;
        }
#endif

        private static List<FrameCategoryBreakdown> GenerateCategoryBreakdown(FrameSnapshot snapshot)
        {
            var dict = new Dictionary<ProfilerCategory, float>();
            foreach (ProfilerCategory cat in Enum.GetValues(typeof(ProfilerCategory)))
            {
                dict[cat] = 0f;
            }

            // Aggregate self time instead of total time to avoid double counting overlaps
            foreach (var top in snapshot.TopLevelSamples)
            {
                foreach(var s in top.Flatten())
                {
                    dict[s.Category] += s.SelfTimeMs;
                }
            }

            var breakdown = new List<FrameCategoryBreakdown>();
            float totalSelfMs = 0f;
            foreach (var kvp in dict) totalSelfMs += kvp.Value;

            foreach (var kvp in dict)
            {
                if (kvp.Value > 0.01f) // Bỏ qua nếu quá nhỏ bé (nhỏ hơn 0.01ms)
                {
                    breakdown.Add(new FrameCategoryBreakdown
                    {
                        Category = kvp.Key,
                        TimeMs   = kvp.Value,
                        Percentage = totalSelfMs > 0 ? (kvp.Value / totalSelfMs) : 0f
                    });
                }
            }

            breakdown.Sort((a, b) => b.Percentage.CompareTo(a.Percentage));
            return breakdown;
        }

        private static void RegisterToLookup(
            Dictionary<string, List<ProfilerSample>> lookup, ProfilerSample sample)
        {
            // Đăng ký sample vào dictionary (case-insensitive)
            string key = sample.Name;
            if (!lookup.TryGetValue(key, out var list))
            {
                list = new List<ProfilerSample>();
                lookup[key] = list;
            }
            list.Add(sample);

            // Đệ quy cho children
            foreach (var child in sample.Children)
                RegisterToLookup(lookup, child);
        }

        private static long CalculateTotalGCAlloc(FrameSnapshot snapshot)
        {
            long total = 0;
            foreach (var top in snapshot.TopLevelSamples)
                foreach (var s in top.Flatten())
                    if (s.GCAllocBytes > 0 && s.Depth <= 2) // tránh double-count
                        total += s.GCAllocBytes;
            return total;
        }

        // ─── Stat Reader ──────────────────────────────────────────────────────

        private static float ReadStatForFrame(string statName, int frameIndex)
        {
            try
            {
                int statId = ProfilerDriver.GetStatisticsIdentifier(statName);
                if (statId < 0) return 0f;

                var values = new float[1];
                ProfilerDriver.GetStatisticsValues(statId, frameIndex, 1.0f, values, out _);
                return values[0];
            }
            catch { return 0f; }
        }

        private static void MarkSpikes(FrameTimeData[] frames, float multiplier)
        {
            if (frames.Length < 5) return;

            // Tính mean
            float sum = 0;
            int   cnt = 0;
            foreach (var f in frames)
                if (f.TotalMs > 0) { sum += f.TotalMs; cnt++; }

            float average = cnt > 0 ? sum / cnt : 16.6f;
            float threshold = average * multiplier;

            for (int i = 0; i < frames.Length; i++)
            {
                var f = frames[i];
                f.IsSpike = f.TotalMs > threshold;
                frames[i] = f;
            }
        }
    }
}
