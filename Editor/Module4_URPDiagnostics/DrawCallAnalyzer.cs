using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module4
{
    /// <summary>
    /// Phân tích số liệu Draw Calls, SetPass Calls, Batches từ Unity Profiler API.
    /// Phân loại nút thắt cổ chai: CPU-Bound / GPU-Bound theo mô hình trong tài liệu.
    /// </summary>
    public static class DrawCallAnalyzer
    {
        [Serializable]
        public class DrawCallStats
        {
            public int  SetPassCalls;
            public int  DrawCalls;
            public int  Batches;
            public int  TrianglesRendered;
            public int  VerticesRendered;
            public bool IsCaptured;
            public string CaptureNote;
        }

        /// <summary>
        /// Lấy draw call statistics từ Unity Editor statistics (chỉ trong Play Mode)
        /// </summary>
        public static DrawCallStats CaptureStats()
        {
            var stats = new DrawCallStats();

#if UNITY_2020_1_OR_NEWER
            if (!Application.isPlaying)
            {
                stats.IsCaptured  = false;
                stats.CaptureNote = "Draw Call stats chỉ khả dụng trong Play Mode.";
                return stats;
            }

            // Dùng UnityStats API để lấy thống kê kết xuất (Editor stats)
            try
            {

                // Frame Stats từ Unity GameObject (fallback method)
                // Unity Stats object — chỉ in Editor
                stats.SetPassCalls       = (int)GetStat("setPassCalls");
                stats.DrawCalls          = (int)GetStat("drawCalls");
                stats.Batches            = (int)GetStat("batches");
                stats.TrianglesRendered  = (int)GetStat("triangles");
                stats.VerticesRendered   = (int)GetStat("vertices");
                stats.IsCaptured         = true;
                stats.CaptureNote        = $"Captured at frame {Time.frameCount}";
            }
            catch
            {
                stats.IsCaptured  = false;
                stats.CaptureNote = "Không thể đọc Profiler stats. Mở Profiler Window và Enable profiling.";
            }
#else
            stats.IsCaptured  = false;
            stats.CaptureNote = "Yêu cầu Unity 2020.1+";
#endif

            return stats;
        }

        /// <summary>
        /// Phân tích stats và sinh issues theo mô hình 3 loại nút thắt
        /// </summary>
        public static List<PerformanceIssue> Analyze(DrawCallStats stats = null)
        {
            var issues = new List<PerformanceIssue>();
            stats ??= CaptureStats();

            if (!stats.IsCaptured)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Info,
                    Title         = "Draw Call Analysis cần chạy trong Play Mode",
                    Description   = stats.CaptureNote,
                    FixSuggestion = "Enter Play Mode và chạy scene, sau đó chạy lại analysis này."
                });
                return issues;
            }

            issues.Add(new PerformanceIssue
            {
                RuleId = "render.counters", EvidenceLevel = "Measured Editor counters", Severity = IssueSeverity.Info,
                Title = "Editor rendering counters (not a bottleneck diagnosis)",
                Description = $"SetPass={stats.SetPassCalls}; DrawCalls={stats.DrawCalls}; Batches={stats.Batches}; Triangles={stats.TrianglesRendered}; Vertices={stats.VerticesRendered}. {stats.CaptureNote}",
                FixSuggestion = "Compare CPU/GPU timing on the target device. Counts alone cannot classify CPU/GPU bound or certify a universal budget."
            });
            return issues;
        }

        /// <summary>
        /// Đọc Unity Editor Stats via reflection (Editor-only)
        /// </summary>
        private static float GetStat(string statName)
        {
            var property = typeof(UnityEditor.UnityStats).GetProperty(statName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (property == null) throw new MissingMemberException(typeof(UnityEditor.UnityStats).FullName, statName);
            return Convert.ToSingle(property.GetValue(null));
        }
    }
}
