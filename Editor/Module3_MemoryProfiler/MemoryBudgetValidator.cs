using System.Collections.Generic;
using UnityEngine;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module3
{
    /// <summary>
    /// So sánh dữ liệu bộ nhớ runtime với ngân sách RAM đã cấu hình.
    /// Phân loại: Texture budget / Mesh budget / Heap budget.
    /// Sinh Action Items khi vượt ngưỡng.
    /// </summary>
    public static class MemoryBudgetValidator
    {
        [System.Serializable]
        public class MemoryUsageSnapshot
        {
            public float TotalResidentMB;
            public float TextureMemoryMB;
            public float MeshMemoryMB;
            public float AudioMemoryMB;
            public float ManagedHeapMB;
            public float ReservedMB;
            public int   TextureCount;
            public int   MeshCount;
        }

        /// <summary>
        /// Lấy usage bộ nhớ hiện tại từ Unity Profiler API
        /// </summary>
        public static MemoryUsageSnapshot CaptureCurrentUsage()
        {
            var snapshot = new MemoryUsageSnapshot();

#if UNITY_2020_1_OR_NEWER
            // Profiler.GetTotalReservedMemoryLong() — tổng bộ nhớ được bảo lưu
            snapshot.TotalResidentMB  = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong()   / 1048576f;
            snapshot.ManagedHeapMB    = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong()           / 1048576f;
            snapshot.ReservedMB       = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong()    / 1048576f;
#endif

            // Thu thập Texture và Mesh statistics
            var textures = Resources.FindObjectsOfTypeAll<Texture>();
            var meshes   = Resources.FindObjectsOfTypeAll<Mesh>();

            snapshot.TextureCount = textures.Length;
            snapshot.MeshCount    = meshes.Length;

            float texMem = 0f;
            foreach (var tex in textures)
                texMem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex) / 1048576f;

            float meshMem = 0f;
            foreach (var mesh in meshes)
                meshMem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh) / 1048576f;

            snapshot.TextureMemoryMB = texMem;
            snapshot.MeshMemoryMB    = meshMem;

            return snapshot;
        }

        /// <summary>
        /// Validate usage so với budget và sinh issues
        /// </summary>
        public static List<PerformanceIssue> Validate(MemoryUsageSnapshot usage = null)
        {
            var issues = new List<PerformanceIssue>();

            if (!Application.isPlaying)
            {
                // Runtime check — chỉ có ý nghĩa khi đang Play
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Info,
                    Title         = "Memory Budget Validation cần chạy trong Play Mode",
                    Description   = "Validation so sánh bộ nhớ thực tế với budget cấu hình. " +
                                    "Nhấn Play trong Unity Editor, sau đó chạy lại kiểm tra này.",
                    FixSuggestion = "Enter Play Mode, chạy một vòng gameplay đại diện, rồi chụp Snapshot để validate."
                });
                return issues;
            }

            usage ??= CaptureCurrentUsage();

            float texBudget  = PlatformConfig.TextureBudgetMB;
            float meshBudget = PlatformConfig.MeshBudgetMB;
            float heapBudget = PlatformConfig.ManagedHeapBudgetMB;
            float totalBudget = PlatformConfig.AvailableBudgetMB;

            // ─── Texture Budget ─────────────────────────────────────────────
            if (usage.TextureMemoryMB > texBudget)
            {
                float overPercent = (usage.TextureMemoryMB - texBudget) / texBudget * 100f;
                issues.Add(new PerformanceIssue
                {
                    Severity      = overPercent > 30f ? IssueSeverity.Error : IssueSeverity.Warning,
                    Title         = $"Texture Memory vượt ngân sách: {usage.TextureMemoryMB:F0}MB / {texBudget:F0}MB (+{overPercent:F0}%)",
                    Description   = $"Texture({usage.TextureCount}) đang chiếm {usage.TextureMemoryMB:F0}MB — vượt budget {texBudget:F0}MB " +
                                    $"cho Android {PlatformConfig.AndroidPhysicalRamMB}MB tier.",
                    FixSuggestion = $"1. Giảm MaxSize xuống {PlatformConfig.GetRecommendedMaxTextureSize()}px.\n" +
                                    $"2. Áp dụng ASTC {PlatformConfig.GetRecommendedASTCBlockSize()} cho textures lớn.\n" +
                                    $"3. Dùng AssetBundle với HD/SD variants: HD cho 6GB devices, SD cho 2GB."
                });
            }
            else
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Info,
                    Title         = $"✓ Texture Budget OK: {usage.TextureMemoryMB:F0}MB / {texBudget:F0}MB",
                    Description   = "Bộ nhớ texture trong ngưỡng an toàn.",
                    FixSuggestion = "Không cần hành động."
                });
            }

            // ─── Mesh Budget ────────────────────────────────────────────────
            if (usage.MeshMemoryMB > meshBudget)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Warning,
                    Title         = $"Mesh Memory vượt ngân sách: {usage.MeshMemoryMB:F0}MB / {meshBudget:F0}MB",
                    Description   = $"Mesh data ({usage.MeshCount} meshes) tiêu thụ {usage.MeshMemoryMB:F0}MB.",
                    FixSuggestion = "1. Thiết lập LOD Groups để giảm poly ở xa camera.\n" +
                                    "2. Tắt Read/Write Enabled trên Mesh không cần edit at runtime.\n" +
                                    "3. Mục tiêu: 1-2 triệu triangles tổng cho mobile 60fps."
                });
            }

            // ─── Managed Heap Budget ─────────────────────────────────────────
            if (usage.ManagedHeapMB > heapBudget)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Error,
                    Title         = $"Managed Heap vượt ngân sách: {usage.ManagedHeapMB:F0}MB / {heapBudget:F0}MB",
                    Description   = $"Managed Heap đang phình tới {usage.ManagedHeapMB:F0}MB. " +
                                    $"Dấu hiệu của memory fragmentation hoặc tích lũy GC objects.",
                    FixSuggestion = "1. Implement Object Pooling cho mọi object spawn/despawn thường xuyên.\n" +
                                    "2. Dùng struct thay vì class cho data containers nhỏ.\n" +
                                    "3. Tránh String allocation trong Update(). Dùng StringBuilder.\n" +
                                    "4. Gọi GC.Collect() + Resources.UnloadUnusedAssets() sau khi load scene."
                });
            }

            // ─── Total Budget Check (70% rule) ──────────────────────────────
            if (usage.TotalResidentMB > totalBudget * 0.90f)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Error,
                    Title         = $"CRITICAL: Tổng Resident Memory ({usage.TotalResidentMB:F0}MB) gần vượt 70% ngân sách ({totalBudget:F0}MB)",
                    Description   = "Nguy cơ cao bị iOS Jetsam kill app hoặc Android OOM Eviction. " +
                                    "Hệ điều hành sẽ đóng ứng dụng mà không cảnh báo.",
                    FixSuggestion = "ƯU TIÊN KHẨN CẤP: Giảm Texture+Mesh+Audio xuống mức budget. " +
                                    "Xem Action Items từ Asset Audit để xác định tài nguyên tiêu tốn nhiều nhất."
                });
            }

            return issues;
        }
    }
}
