using UnityEngine;

namespace Optifunity.Runtime
{
    /// <summary>
    /// ScriptableObject chứa cấu hình ngân sách RAM cho từng tier thiết bị.
    /// Được tạo qua menu: Assets > Create > Optifunity > Memory Budget
    /// </summary>
    [CreateAssetMenu(fileName = "MemoryBudget", menuName = "Optifunity/Memory Budget", order = 1)]
    public class MemoryBudget : ScriptableObject
    {
        [Header("Target Platform")]
        [Tooltip("Nền tảng mục tiêu của dự án")]
        public TargetPlatformType targetPlatform = TargetPlatformType.Android;

        [Header("Android RAM Tiers (MB)")]
        [Tooltip("RAM vật lý thực tế của thiết bị Android mục tiêu (MB). Ví dụ: 2048, 4096, 6144")]
        public int androidPhysicalRamMB = 4096;

        [Header("iOS Safety Factor")]
        [Tooltip("Hệ số an toàn cho iOS Jetsam mechanism (0.0 - 1.0). Khuyến nghị: 0.5 - 0.6")]
        [Range(0.3f, 0.7f)]
        public float iosJetsamSafetyFactor = 0.55f;

        [Header("Budget Override (leave 0 = Auto)")]
        [Tooltip("Override thủ công ngân sách Texture (MB). 0 = tính tự động")]
        public int textureBudgetOverrideMB = 0;
        [Tooltip("Override thủ công ngân sách Mesh (MB). 0 = tính tự động")]
        public int meshBudgetOverrideMB = 0;
        [Tooltip("Override thủ công ngân sách Managed Heap (MB). 0 = tính tự động")]
        public int managedHeapBudgetOverrideMB = 0;

        // --- Computed Properties ---

        /// <summary>70% của RAM vật lý (quy luật cốt lõi)</summary>
        public float AvailableBudgetMB => androidPhysicalRamMB * 0.70f;

        /// <summary>Ngân sách Texture (khoảng 40% của available budget)</summary>
        public float TextureBudgetMB =>
            textureBudgetOverrideMB > 0 ? textureBudgetOverrideMB
            : CalculateTextureBudget();

        /// <summary>Ngân sách Mesh geometry (khoảng 15% của available budget)</summary>
        public float MeshBudgetMB =>
            meshBudgetOverrideMB > 0 ? meshBudgetOverrideMB
            : AvailableBudgetMB * 0.15f;

        /// <summary>Ngân sách Managed Heap + khác (khoảng 20% của available budget)</summary>
        public float ManagedHeapBudgetMB =>
            managedHeapBudgetOverrideMB > 0 ? managedHeapBudgetOverrideMB
            : AvailableBudgetMB * 0.20f;

        private float CalculateTextureBudget()
        {
            // Tier-based allocation dựa trên bảng trong tài liệu
            if (androidPhysicalRamMB <= 2048)
                return 80f;   // 2GB tier: ~80MB textures
            if (androidPhysicalRamMB <= 4096)
                return 150f;  // 4GB tier: ~150MB textures
            return 250f;      // 6GB+ tier: ~250MB textures
        }

        /// <summary>Lấy ngưỡng cảnh báo theo tier Android</summary>
        public AndroidDeviceTier GetAndroidTier()
        {
            if (androidPhysicalRamMB <= 2048) return AndroidDeviceTier.Low;
            if (androidPhysicalRamMB <= 4096) return AndroidDeviceTier.Mid;
            return AndroidDeviceTier.High;
        }

        /// <summary>Cấu hình ASTC block size khuyến nghị theo tier</summary>
        public string GetRecommendedASTCBlockSize()
        {
            return GetAndroidTier() switch
            {
                AndroidDeviceTier.Low  => "8x8 hoặc 10x10 (Priority: giảm VRAM)",
                AndroidDeviceTier.Mid  => "6x6 (Cân bằng chất lượng - dung lượng)",
                AndroidDeviceTier.High => "4x4 (Priority: chất lượng hình ảnh cao nhất)",
                _ => "6x6"
            };
        }

        /// <summary>MaxTexture Size khuyến nghị theo tier</summary>
        public int GetRecommendedMaxTextureSize()
        {
            return GetAndroidTier() switch
            {
                AndroidDeviceTier.Low  => 512,
                AndroidDeviceTier.Mid  => 1024,
                AndroidDeviceTier.High => 2048,
                _ => 1024
            };
        }
    }

    public enum TargetPlatformType
    {
        Android,
        iOS,
        PC,
        Console
    }

    public enum AndroidDeviceTier
    {
        Low,   // ≤ 2GB RAM
        Mid,   // ≤ 4GB RAM
        High   // > 6GB RAM
    }
}
