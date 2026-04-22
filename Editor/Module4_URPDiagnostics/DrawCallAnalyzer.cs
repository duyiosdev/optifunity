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

        // Giới hạn an toàn cho Mobile (căn cứ theo tài liệu)
        private const int MOBILE_SETPASS_WARNING  = 500;
        private const int MOBILE_SETPASS_CRITICAL = 1000;
        private const int MOBILE_BATCH_WARNING    = 500;
        private const int MOBILE_TRI_WARNING_M    = 2_000_000; // 2 triệu triangles
        private const int PC_SETPASS_WARNING      = 2000;

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
                stats.SetPassCalls       = (int)GetStat("SetPass Calls");
                stats.DrawCalls          = (int)GetStat("Draw Calls");
                stats.Batches            = (int)GetStat("Batches");
                stats.TrianglesRendered  = (int)GetStat("Triangles");
                stats.VerticesRendered   = (int)GetStat("Vertices");
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

            bool isMobile = PlatformConfig.IsMobile;
            int setpassLimit = isMobile ? MOBILE_SETPASS_CRITICAL : PC_SETPASS_WARNING;
            int setpassWarn  = isMobile ? MOBILE_SETPASS_WARNING   : PC_SETPASS_WARNING / 2;

            // ─── 1. SetPass Calls (gánh nặng CPU Render Thread) ────────────────
            if (stats.SetPassCalls > setpassLimit)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity     = IssueSeverity.Error,
                    Title        = $"CRITICAL CPU Render Thread: SetPass Calls = {stats.SetPassCalls} (giới hạn: {setpassLimit})",
                    Description  = $"SetPass Calls vượt ngưỡng nghiêm trọng cho {(isMobile ? "Mobile" : "PC")}. " +
                                   $"CPU phải thay đổi Shader state {stats.SetPassCalls} lần/frame, " +
                                   $"GPU phải chờ CPU — đây là dạng CPU-Bound Render Thread.",
                    FixSuggestion = "1. Kiểm tra SRP Batcher đang bật và tất cả Shaders tương thích CBUFFER.\n" +
                                    "2. Hợp nhất Materials có cùng Shader về một Material.\n" +
                                    "3. Dùng Material Property Block thay vì tạo Material instance riêng.\n" +
                                    "4. Bật GPU Instancing cho objects giống nhau (cỏ, cây, thiên thạch)."
                });
            }
            else if (stats.SetPassCalls > setpassWarn)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Warning,
                    Title         = $"SetPass Calls cao: {stats.SetPassCalls}/{setpassLimit}",
                    Description   = $"SetPass đang ở mức cảnh báo. Nên theo dõi và tối ưu hóa.",
                    FixSuggestion = "Kiểm tra Frame Debugger: xác định Shader variant nào đang gây nhiều SetPass nhất."
                });
            }
            else
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Info,
                    Title         = $"✓ SetPass Calls OK: {stats.SetPassCalls}/{setpassLimit}",
                    Description   = "SetPass Calls trong ngưỡng an toàn.",
                    FixSuggestion = "Không cần hành động."
                });
            }

            // ─── 2. Draw Calls / Batches ───────────────────────────────────────
            if (isMobile && stats.Batches > MOBILE_BATCH_WARNING)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Warning,
                    Title         = $"Draw Call Batches cao cho Mobile: {stats.Batches}",
                    Description   = $"Số lượng Batches = {stats.Batches}. Mục tiêu cho mobile 60fps: < 500 batches.",
                    FixSuggestion = "1. Kết hợp Static Batching cho objects tĩnh.\n" +
                                    "2. GPU Instancing cho objects lặp nhiều lần.\n" +
                                    "3. Giảm số lượng Materials khác nhau trong scene."
                });
            }

            // ─── 3. Triangle Count ─────────────────────────────────────────────
            if (isMobile && stats.TrianglesRendered > MOBILE_TRI_WARNING_M)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Warning,
                    Title         = "Triangle Count vượt ngưỡng Mobile",
                    Description   = $"Hiển thị {stats.TrianglesRendered / 1000000f:F1}M triangles. " +
                                    $"Tài liệu khuyến nghị {MOBILE_TRI_WARNING_M / 1000000f:F0}-{MOBILE_TRI_WARNING_M * 2 / 1000000f:F0}M triangles cho mobile 60fps.",
                    FixSuggestion = "1. Thiết lập LOD Groups (LOD0/LOD1/LOD2) cho character và environmental objects.\n" +
                                    "2. Dùng Occlusion Culling để loại bỏ objects bị che khuất.\n" +
                                    "3. Giảm poly density của background assets."
                });
            }

            // ─── 4. Bottleneck Classification ─────────────────────────────────
            ClassifyBottleneck(issues, stats, isMobile);

            return issues;
        }

        /// <summary>
        /// Phân loại nút thắt cổ chai theo mô hình CPU-Bound / GPU-Bound từ tài liệu
        /// </summary>
        private static void ClassifyBottleneck(List<PerformanceIssue> issues, DrawCallStats stats, bool isMobile)
        {
            string bottleneckType;
            string description;
            string fix;

            int setpassLimit = isMobile ? MOBILE_SETPASS_CRITICAL : PC_SETPASS_WARNING;

            if (stats.SetPassCalls > setpassLimit)
            {
                bottleneckType = "CPU-Bound (Render Thread)";
                description    = $"GPU đang chờ CPU thiết lập render state (SetPass Calls = {stats.SetPassCalls}). " +
                                 $"Đây là dạng nghẽn phổ biến khi có nhiều Materials/Shaders khác nhau.";
                fix            = "Tối ưu hóa SRP Batcher compatibility. Hợp nhất Materials/Shaders.";
            }
            else if (stats.TrianglesRendered > MOBILE_TRI_WARNING_M * 1.5f && isMobile)
            {
                bottleneckType = "GPU-Bound (Vertex/Fill Rate)";
                description    = $"GPU quá tải do geometry quá nhiều hoặc Fragment Shader phức tạp. " +
                                 $"Dấu hiệu: CPU idle nhưng GfxWait xuất hiện trong Profiler.";
                fix            = "Giảm poly count qua LOD. Tắt Depth Priming (Mobile). Giảm Shadow Resolution.";
            }
            else
            {
                bottleneckType = "Balanced";
                description    = "Không phát hiện nút thắt rõ ràng từ Draw Call metrics.";
                fix            = "Tiếp tục monitoring. Dùng Frame Debugger để phân tích chi tiết hơn.";
            }

            issues.Add(new PerformanceIssue
            {
                Severity      = bottleneckType == "Balanced" ? IssueSeverity.Info : IssueSeverity.Warning,
                Title         = "Phân loại nút thắt cổ chai",
                Description   = $"Dựa trên chỉ số render: {bottleneckType}. {description}",
                FixSuggestion = fix
            });
        }

        /// <summary>
        /// Đọc Unity Editor Stats via reflection (Editor-only)
        /// </summary>
        private static float GetStat(string statName)
        {
            try
            {
                // Unity Editor exposes stats via UnityStats internal class
                var unityStatsType = Type.GetType("UnityEditor.UnityStats, UnityEditor");
                if (unityStatsType == null) return 0;

                var prop = unityStatsType.GetProperty(statName.Replace(" ", ""),
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null) return Convert.ToSingle(prop.GetValue(null));

                return 0;
            }
            catch { return 0; }
        }
    }
}
