using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Optifunity.Editor.Module3
{
    /// <summary>
    /// Phân tích dữ liệu từ Memory Snapshots đã chụp.
    /// Thực hiện Differential Analysis giữa Baseline và Teardown snapshots
    /// để phát hiện objects bị "rò rỉ" (bị kẹt không giải phóng).
    /// </summary>
    public static class SnapshotAnalyzer
    {
        [Serializable]
        public class SnapshotSummary
        {
            public string FilePath;
            public string SnapshotType;
            public long   FileSizeBytes;
            public string CaptureTime;
            // Metadata được đọc từ tên file
            public string SceneName;
            public string PlatformTarget;
        }

        [Serializable]
        public class DiffReport
        {
            public string BaselinePath;
            public string TeardownPath;
            public List<LeakedObjectEntry> LeakedObjects = new();
            public bool   HasLeaks => LeakedObjects.Count > 0;

            // Summary metrics từ file info
            public long BaselineFileSizeBytes;
            public long TeardownFileSizeBytes;
            public long SizeDeltaBytes;
        }

        [Serializable]
        public class LeakedObjectEntry
        {
            public string TypeName;
            public string ObjectName;
            public string EstimatedSize;
            public string SuspectedCause;
        }

        /// <summary>
        /// Lấy danh sách tóm tắt tất cả snapshots đã chụp
        /// </summary>
        public static List<SnapshotSummary> GetAllSnapshotSummaries()
        {
            var summaries = new List<SnapshotSummary>();
            var files = SnapshotCapturer.GetAllSnapshots();

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                summaries.Add(new SnapshotSummary
                {
                    FilePath      = file,
                    FileSizeBytes = info.Length,
                    CaptureTime   = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    SnapshotType  = ParseSnapshotType(info.Name),
                    SceneName     = ParseSceneName(info.Name)
                });
            }

            summaries.Sort((a, b) => string.Compare(a.CaptureTime, b.CaptureTime, StringComparison.Ordinal));
            return summaries;
        }

        /// <summary>
        /// Phân tích Differential giữa Baseline và Teardown snapshot.
        /// Vì không có Memory Profiler open API để đọc .snap programmatically trong Editor scripts,
        /// ta phân tích qua file size delta và metadata để cung cấp actionable insights.
        /// </summary>
        public static DiffReport AnalyzeDiff(string baselinePath, string teardownPath)
        {
            var report = new DiffReport
            {
                BaselinePath = baselinePath,
                TeardownPath = teardownPath
            };

            if (!File.Exists(baselinePath) || !File.Exists(teardownPath))
            {
                Debug.LogWarning("[Optifunity] Không tìm thấy snapshot files để so sánh.");
                return report;
            }

            var baselineInfo = new FileInfo(baselinePath);
            var teardownInfo = new FileInfo(teardownPath);

            report.BaselineFileSizeBytes = baselineInfo.Length;
            report.TeardownFileSizeBytes = teardownInfo.Length;
            report.SizeDeltaBytes        = teardownInfo.Length - baselineInfo.Length;

            // Nếu file Teardown lớn hơn Baseline đáng kể, có thể có leak
            float deltaPercent = report.BaselineFileSizeBytes > 0
                ? (float)report.SizeDeltaBytes / report.BaselineFileSizeBytes * 100f
                : 0f;

            if (deltaPercent > 10f)
            {
                report.LeakedObjects.Add(new LeakedObjectEntry
                {
                    TypeName       = "Unknown (Snapshot Size Delta)",
                    ObjectName     = $"Snapshot tăng {deltaPercent:F1}% sau teardown",
                    EstimatedSize  = $"{report.SizeDeltaBytes / 1024f:F0} KB",
                    SuspectedCause = "Có thể có objects chưa được giải phóng. " +
                                     "Mở Memory Profiler Package window để xem chi tiết Paths to Root."
                });
            }

            // Sinh recommendations dựa trên pattern từ tài liệu
            AddPatternBasedInsights(report, deltaPercent);

            return report;
        }

        /// <summary>
        /// Tạo danh sách PerformanceIssues từ DiffReport để tích hợp vào ReportEngine
        /// </summary>
        public static List<Core.PerformanceIssue> GenerateIssues(DiffReport diff)
        {
            var issues = new List<Core.PerformanceIssue>();

            if (!diff.HasLeaks && diff.SizeDeltaBytes < 0)
            {
                // Bộ nhớ giảm sau teardown — tốt
                issues.Add(new Core.PerformanceIssue
                {
                    Severity     = Core.IssueSeverity.Info,
                    Title        = "Memory Snapshot: Scene dọn dẹp bộ nhớ tốt",
                    Description  = $"Teardown snapshot nhỏ hơn Baseline {-diff.SizeDeltaBytes / 1024f:F0}KB. " +
                                   $"Scene đã giải phóng tài nguyên đúng cách.",
                    FixSuggestion = "Không cần hành động thêm. Tiếp tục monitoring để đảm bảo consistency."
                });
                return issues;
            }

            foreach (var leaked in diff.LeakedObjects)
            {
                float deltaPercent = diff.BaselineFileSizeBytes > 0
                    ? (float)diff.SizeDeltaBytes / diff.BaselineFileSizeBytes * 100f : 0f;

                var severity = deltaPercent > 25f ? Core.IssueSeverity.Error : Core.IssueSeverity.Warning;

                issues.Add(new Core.PerformanceIssue
                {
                    Severity      = severity,
                    Title         = $"Memory Leak tiềm năng: {leaked.TypeName} — {leaked.EstimatedSize}",
                    Description   = leaked.ObjectName + "\n" + leaked.SuspectedCause,
                    AssetPath     = diff.TeardownPath,
                    FixSuggestion = "Mở Memory Profiler Package Window → Compare Snapshots để xem Paths to Root. " +
                                    "Kiểm tra: (1) Static events chưa unsubscribe, (2) Coroutines vô hạn, " +
                                    "(3) ScriptableObject references, (4) Resources không gọi UnloadUnusedAssets()."
                });
            }

            // Issue cho Object Pooling nếu heap phình to
            if (diff.SizeDeltaBytes > 5 * 1024 * 1024) // > 5MB delta
            {
                issues.Add(new Core.PerformanceIssue
                {
                    Severity      = Core.IssueSeverity.Warning,
                    Title         = "Khuyến nghị: Implement Object Pooling để giảm GC Spikes",
                    Description   = $"Memory delta lớn ({diff.SizeDeltaBytes / 1048576f:F1}MB) cho thấy nhiều allocation/deallocation. " +
                                    $"Object Pooling có thể loại bỏ hoàn toàn GC Spikes.",
                    FixSuggestion = "Triển khai GenericPool<T> hoặc dùng Unity's ObjectPool<T> (Unity 2021+) " +
                                    "cho Bullets, Particles, UI elements được spawn/despawn thường xuyên."
                });
            }

            return issues;
        }

        // ─── Private Helpers ──────────────────────────────────────────────────

        private static void AddPatternBasedInsights(DiffReport report, float deltaPercent)
        {
            if (deltaPercent > 5f)
            {
                report.LeakedObjects.Add(new LeakedObjectEntry
                {
                    TypeName       = "Texture2D (suspected)",
                    ObjectName     = "Texture chưa được UnloadUnusedAssets()",
                    EstimatedSize  = "Không xác định — xem trong Memory Profiler UI",
                    SuspectedCause = "Texture2D/Mesh/AudioClip còn tham chiếu sau khi Scene unload. " +
                                     "Gọi Resources.UnloadUnusedAssets() cuối scene và đảm bảo không giữ static reference."
                });
            }
        }

        private static string ParseSnapshotType(string fileName)
        {
            if (fileName.Contains("Baseline"))  return "Baseline";
            if (fileName.Contains("PeakLoad"))  return "PeakLoad";
            if (fileName.Contains("Teardown"))  return "Teardown";
            return "Manual";
        }

        private static string ParseSceneName(string fileName)
        {
            // Format: Snapshot_{type}_{timestamp}.snap
            // Scene name được lưu trong metadata bên trong file
            return "N/A";
        }
    }
}
