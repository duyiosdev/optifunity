using System;
using System.Collections.Generic;
using UnityEngine;

namespace Optifunity.Editor.Module5
{
    // ══════════════════════════════════════════════════════════════════════════
    // Phân loại 11 loại spike bottleneck + Balanced + Unknown
    // ══════════════════════════════════════════════════════════════════════════
    public enum BottleneckType
    {
        Unknown             = 0,
        Balanced            = 1,   // Frame bình thường, không có nút thắt rõ ràng
        GarbageCollection   = 2,   // GC.Collect kích hoạt
        GPUBound            = 3,   // Gfx.WaitForPresent cao
        CPURenderThread     = 4,   // Camera.Render / SetPass Calls quá nhiều
        Physics             = 5,   // Physics.Processing hoặc FetchResults
        Scripting           = 6,   // BehaviourUpdate / MonoBehaviour scripts
        UICanvas            = 7,   // Canvas.BuildBatch / RebuildAll
        Animation           = 8,   // Animator.Update / AnimationClip sampling
        AssetLoading        = 9,   // Loading.ReadObject trên main thread
        Audio               = 10,  // AudioManager.Update
        VSync               = 11,  // WaitForTargetFPS
        JobSystemStall      = 12,  // Job thread stall / WorkStealing
        Mixed               = 99   // Nhiều factors cùng lúc
    }

    public enum SpikeSeverity
    {
        Low      = 0,   // < 1.5x average
        Medium   = 1,   // 1.5x - 2x average
        High     = 2,   // 2x - 4x average
        Critical = 3    // > 4x average
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Một profiler sample từ hierarchy frame data
    // ══════════════════════════════════════════════════════════════════════════
    [Serializable]
    public class ProfilerSample
    {
        public string Name;
        public float  TotalTimeMs;
        public float  SelfTimeMs;
        public long   GCAllocBytes;
        public int    CallCount;
        public float  PercentOfFrame;   // TotalTimeMs / FrameTotalMs * 100
        public int    Depth;

        public List<ProfilerSample> Children = new();

        // Flat traversal helpers
        public IEnumerable<ProfilerSample> Flatten()
        {
            yield return this;
            foreach (var child in Children)
                foreach (var s in child.Flatten())
                    yield return s;
        }

        public override string ToString() =>
            $"{Name} [{TotalTimeMs:F2}ms | Self:{SelfTimeMs:F2}ms | GC:{GCAllocBytes}B | x{CallCount}]";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Snapshot đầy đủ của một frame cụ thể
    // ══════════════════════════════════════════════════════════════════════════
    [Serializable]
    public class FrameSnapshot
    {
        public int    FrameIndex;
        public float  TotalCpuTimeMs;
        public float  TotalGpuTimeMs;     // 0 nếu không có GPU profiler
        public long   TotalGCAllocBytes;
        public bool   IsValid;
        public string ErrorMessage;

        public List<ProfilerSample> TopLevelSamples = new();

        // Flat lookup: indexed by marker name (lowercase), nhanh O(1)
        public Dictionary<string, List<ProfilerSample>> SamplesByName
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Tìm tất cả samples có tên khớp (case-insensitive), kể cả nested</summary>
        public List<ProfilerSample> FindAll(string markerName)
        {
            if (SamplesByName.TryGetValue(markerName, out var list)) return list;
            return new List<ProfilerSample>();
        }

        /// <summary>Tổng thời gian của tất cả samples khớp marker</summary>
        public float SumTime(string markerName)
        {
            float total = 0f;
            foreach (var s in FindAll(markerName)) total += s.TotalTimeMs;
            return total;
        }

        /// <summary>SelfTime tổng của tất cả samples khớp marker</summary>
        public float SumSelfTime(string markerName)
        {
            float total = 0f;
            foreach (var s in FindAll(markerName)) total += s.SelfTimeMs;
            return total;
        }

        /// <summary>Kiểm tra marker có tồn tại trong frame không</summary>
        public bool HasMarker(string markerName) =>
            SamplesByName.ContainsKey(markerName);

        /// <summary>Lấy top N samples tốn kém nhất (theo TotalTimeMs)</summary>
        public List<ProfilerSample> GetTopByTime(int count = 10)
        {
            var all = new List<ProfilerSample>();
            foreach (var top in TopLevelSamples)
                foreach (var s in top.Flatten())
                    all.Add(s);

            all.Sort((a, b) => b.TotalTimeMs.CompareTo(a.TotalTimeMs));
            return all.Count > count ? all.GetRange(0, count) : all;
        }

        /// <summary>Lấy top N samples phân bổ GC nhiều nhất</summary>
        public List<ProfilerSample> GetTopByGCAlloc(int count = 10)
        {
            var all = new List<ProfilerSample>();
            foreach (var top in TopLevelSamples)
                foreach (var s in top.Flatten())
                    if (s.GCAllocBytes > 0)
                        all.Add(s);

            all.Sort((a, b) => b.GCAllocBytes.CompareTo(a.GCAllocBytes));
            return all.Count > count ? all.GetRange(0, count) : all;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Kết quả phân tích một frame spike
    // ══════════════════════════════════════════════════════════════════════════
    [Serializable]
    public class SpikeAnalysisReport
    {
        public int           FrameIndex;
        public float         FrameTotalMs;
        public float         AverageFrameMs;   // Rolling 300-frame average
        public bool          IsSpike;          // FrameTotalMs > 1.5x average
        public SpikeSeverity Severity;

        // Primary bottleneck
        public BottleneckType PrimaryBottleneck;
        public float          PrimaryConfidence; // 0.0 - 1.0
        public string         PrimaryTitle;
        public string         PrimaryDescription;

        // Contributing factors (thứ cấp)
        public List<BottleneckFinding> ContributingFactors = new();

        // Raw evidence data
        public List<ProfilerSample> TopSlowSamples      = new(); // Top 10 by time
        public List<ProfilerSample> TopGCAllocSamples   = new(); // Top 5 by GC

        // Recommendations
        public List<string> Recommendations = new();

        // Kinh nghiệm thực tế từ data
        public List<string> KeyMetrics = new();

        public string SummaryLine =>
            $"Frame #{FrameIndex} | {FrameTotalMs:F2}ms" +
            (IsSpike ? $" ⚠ SPIKE {Severity}" : " ✓ Normal") +
            $" | {PrimaryBottleneck}";
    }

    [Serializable]
    public class BottleneckFinding
    {
        public BottleneckType Type;
        public float          Confidence;    // 0-1
        public string         Evidence;      // Mô tả con số cụ thể
        public float          TimeMs;        // Thời gian tiêu thụ
        public string         Fix;           // Gợi ý sửa ngắn gọn
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Dữ liệu frame time cho timeline chart (lightweight)
    // ══════════════════════════════════════════════════════════════════════════
    public struct FrameTimeData
    {
        public int   FrameIndex;
        public float TotalMs;
        public bool  IsSpike;
    }
}
