using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

#if UNITY_2021_1_OR_NEWER
using Unity.Profiling.Editor;
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

            // Lấy frame time từ stat thay vì toàn bộ hierarchy — nhanh hơn nhiều
            int statId = ProfilerDriver.GetStatisticsIdentifier(STAT_CPU_MAIN_THREAD);
            if (statId < 0)
            {
                // Fallback: set all 0
                for (int i = 0; i < count; i++)
                    result[i] = new FrameTimeData { FrameIndex = firstFrame + i };
                return result;
            }

            var values = new float[count];
            float maxVal;
            ProfilerDriver.GetStatisticsValues(statId, firstFrame, 1.0f, values, out maxVal);

            for (int i = 0; i < count; i++)
            {
                result[i] = new FrameTimeData
                {
                    FrameIndex = firstFrame + i,
                    TotalMs    = values[i],
                    IsSpike    = false // sẽ được tính sau khi có average
                };
            }

            // Tính rolling average để mark spikes
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
                Depth          = depth
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
#endif

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
