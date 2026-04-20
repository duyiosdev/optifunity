using System;
using System.Collections.Generic;
using UnityEngine;
using Optifunity.Runtime;

namespace Optifunity.Editor.Core
{
    /// <summary>
    /// Cấu hình nền tảng và quản lý ngân sách RAM tập trung.
    /// Lưu trữ các thiết lập qua EditorPrefs để persist giữa các lần mở Unity.
    /// </summary>
    public static class PlatformConfig
    {
        private const string PREF_PLATFORM        = "Optifunity_Platform";
        private const string PREF_ANDROID_RAM     = "Optifunity_AndroidRamMB";
        private const string PREF_IOS_FACTOR      = "Optifunity_iOSSafetyFactor";
        private const string PREF_AUTOFIX_ENABLED = "Optifunity_AutoFixEnabled";
        private const string PREF_MAX_TEX_MOBILE  = "Optifunity_MaxTexSizeMobile";
        private const string PREF_AUDIO_STREAM_THRESHOLD = "Optifunity_AudioStreamThresholdSec";

        // ─── Platform ──────────────────────────────────────────────────────────
        public static TargetPlatformType ActivePlatform
        {
            get => (TargetPlatformType)UnityEditor.EditorPrefs.GetInt(PREF_PLATFORM, (int)TargetPlatformType.Android);
            set => UnityEditor.EditorPrefs.SetInt(PREF_PLATFORM, (int)value);
        }

        public static bool IsMobile =>
            ActivePlatform == TargetPlatformType.Android ||
            ActivePlatform == TargetPlatformType.iOS;

        // ─── Android RAM Budget ────────────────────────────────────────────────
        /// <summary>RAM vật lý thiết bị mục tiêu Android (MB)</summary>
        public static int AndroidPhysicalRamMB
        {
            get => UnityEditor.EditorPrefs.GetInt(PREF_ANDROID_RAM, 4096);
            set => UnityEditor.EditorPrefs.SetInt(PREF_ANDROID_RAM, value);
        }

        public static float AvailableBudgetMB         => AndroidPhysicalRamMB * 0.70f;
        public static float TextureBudgetMB           => CalculateTextureBudget();
        public static float MeshBudgetMB              => AvailableBudgetMB * 0.15f;
        public static float ManagedHeapBudgetMB       => AvailableBudgetMB * 0.20f;

        // ─── iOS ───────────────────────────────────────────────────────────────
        public static float iOSSafetyFactor
        {
            get => UnityEditor.EditorPrefs.GetFloat(PREF_IOS_FACTOR, 0.55f);
            set => UnityEditor.EditorPrefs.SetFloat(PREF_IOS_FACTOR, value);
        }

        // ─── Auto-Fix Mode ─────────────────────────────────────────────────────
        public static bool AutoFixEnabled
        {
            get => UnityEditor.EditorPrefs.GetBool(PREF_AUTOFIX_ENABLED, false);
            set => UnityEditor.EditorPrefs.SetBool(PREF_AUTOFIX_ENABLED, value);
        }

        // ─── Asset Thresholds ──────────────────────────────────────────────────
        public static int MaxTextureSizeMobile
        {
            get => UnityEditor.EditorPrefs.GetInt(PREF_MAX_TEX_MOBILE, 1024);
            set => UnityEditor.EditorPrefs.SetInt(PREF_MAX_TEX_MOBILE, value);
        }

        /// <summary>Ngưỡng giây: AudioClip > này sẽ bị cảnh báo nếu không Streaming</summary>
        public static float AudioStreamingThresholdSeconds
        {
            get => UnityEditor.EditorPrefs.GetFloat(PREF_AUDIO_STREAM_THRESHOLD, 5f);
            set => UnityEditor.EditorPrefs.SetFloat(PREF_AUDIO_STREAM_THRESHOLD, value);
        }

        // ─── Computed ─────────────────────────────────────────────────────────
        public static AndroidDeviceTier GetAndroidTier()
        {
            if (AndroidPhysicalRamMB <= 2048) return AndroidDeviceTier.Low;
            if (AndroidPhysicalRamMB <= 4096) return AndroidDeviceTier.Mid;
            return AndroidDeviceTier.High;
        }

        public static string GetRecommendedASTCBlockSize() =>
            GetAndroidTier() switch
            {
                AndroidDeviceTier.Low  => "8x8 / 10x10",
                AndroidDeviceTier.Mid  => "6x6",
                AndroidDeviceTier.High => "4x4",
                _ => "6x6"
            };

        public static int GetRecommendedMaxTextureSize() =>
            GetAndroidTier() switch
            {
                AndroidDeviceTier.Low  => 512,
                AndroidDeviceTier.Mid  => 1024,
                AndroidDeviceTier.High => 2048,
                _ => 1024
            };

        /// <summary>Tóm tắt budget metrics cho UI</summary>
        public static BudgetSummary GetBudgetSummary() => new()
        {
            Platform            = ActivePlatform,
            PhysicalRamMB       = AndroidPhysicalRamMB,
            AvailableBudgetMB   = AvailableBudgetMB,
            TextureBudgetMB     = TextureBudgetMB,
            MeshBudgetMB        = MeshBudgetMB,
            ManagedHeapBudgetMB = ManagedHeapBudgetMB,
            Tier                = GetAndroidTier(),
            RecommendedASTCBlock = GetRecommendedASTCBlockSize(),
            RecommendedMaxTexSize = GetRecommendedMaxTextureSize()
        };

        private static float CalculateTextureBudget()
        {
            return GetAndroidTier() switch
            {
                AndroidDeviceTier.Low  => 80f,
                AndroidDeviceTier.Mid  => 150f,
                AndroidDeviceTier.High => 250f,
                _ => 150f
            };
        }
    }

    [Serializable]
    public struct BudgetSummary
    {
        public TargetPlatformType Platform;
        public int   PhysicalRamMB;
        public float AvailableBudgetMB;
        public float TextureBudgetMB;
        public float MeshBudgetMB;
        public float ManagedHeapBudgetMB;
        public AndroidDeviceTier Tier;
        public string RecommendedASTCBlock;
        public int    RecommendedMaxTexSize;
    }
}
