using System;
using System.IO;
using UnityEditor;
using UnityEngine;

#if UNITY_2021_2_OR_NEWER
using Unity.Profiling.Memory;
#endif

namespace Optifunity.Editor.Module3
{
    /// <summary>
    /// Quản lý việc chụp Memory Snapshots tại các milestone trong Playtest.
    /// Hỗ trợ: Baseline (sau load), Peak Load, Teardown (trước/sau unload scene).
    /// Yêu cầu: package com.unity.memoryprofiler >= 1.0.0
    /// </summary>
    public static class SnapshotCapturer
    {
        public static string SnapshotDirectory =>
            Path.Combine(Application.dataPath, "..", "OptifunitySnapshots");

        /// <summary>Loại snapshot theo milestone</summary>
        public enum SnapshotType { Baseline, PeakLoad, Teardown, Manual }

        // ─── Event thông báo khi snapshot xong ────────────────────────────────
        public static event Action<string, SnapshotType> OnSnapshotCaptured;
        public static event Action<string>               OnSnapshotFailed;

        // ─── State ────────────────────────────────────────────────────────────
        private static bool _isCaptureInProgress;

        /// <summary>
        /// Kiểm tra Memory Profiler package có sẵn không
        /// </summary>
        public static bool IsMemoryProfilerAvailable()
        {
#if UNITY_2021_2_OR_NEWER
            try
            {
                var type = Type.GetType("Unity.Profiling.Memory.MemoryProfiler, Unity.MemoryProfiler");
                return type != null;
            }
            catch { return false; }
#else
            return false;
#endif
        }

        /// <summary>
        /// Chụp Memory Snapshot bất đồng bộ.
        /// Callback OnSnapshotCaptured sẽ được gọi khi hoàn tất.
        /// </summary>
        public static void TakeSnapshot(SnapshotType snapshotType, string customTag = null)
        {
            if (_isCaptureInProgress)
            {
                Debug.LogWarning("[Optifunity] Một snapshot đang được chụp, vui lòng đợi.");
                return;
            }

#if UNITY_2021_2_OR_NEWER
            if (!IsMemoryProfilerAvailable())
            {
                Debug.LogWarning("[Optifunity] Memory Profiler package chưa được cài đặt.\n" +
                                 "Cài đặt qua Package Manager: com.unity.memoryprofiler >= 1.1.0");
                OnSnapshotFailed?.Invoke("Memory Profiler package not found.");
                return;
            }

            Directory.CreateDirectory(SnapshotDirectory);

            string timestamp  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string tag        = customTag ?? snapshotType.ToString();
            string filePath   = Path.Combine(SnapshotDirectory, $"Snapshot_{tag}_{timestamp}.snap");

            _isCaptureInProgress = true;
            Debug.Log($"[Optifunity] Bắt đầu chụp Memory Snapshot [{snapshotType}]...");

            try
            {
                // Đính kèm metadata trước khi chụp
                MemoryProfiler.CreatingMetadata += OnCreatingMetadata;
                MemoryProfiler.TakeSnapshot(
                    filePath,
                    OnSnapshotFinished,
                    CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects | CaptureFlags.NativeAllocations);
            }
            catch (Exception ex)
            {
                _isCaptureInProgress = false;
                MemoryProfiler.CreatingMetadata -= OnCreatingMetadata;
                Debug.LogError($"[Optifunity] Lỗi khi chụp snapshot: {ex.Message}");
                OnSnapshotFailed?.Invoke(ex.Message);
            }

            // ─── Local callbacks ─────────────────────────────────────────────
            void OnSnapshotFinished(string path, bool success)
            {
                _isCaptureInProgress = false;
                MemoryProfiler.CreatingMetadata -= OnCreatingMetadata;

                if (success)
                {
                    Debug.Log($"[Optifunity] Snapshot [{snapshotType}] đã lưu tại: {path}");
                    // Lưu đường dẫn vào EditorPrefs theo loại
                    SaveSnapshotPath(snapshotType, path);
                    OnSnapshotCaptured?.Invoke(path, snapshotType);
                }
                else
                {
                    Debug.LogError($"[Optifunity] Snapshot thất bại.");
                    OnSnapshotFailed?.Invoke("Snapshot capture failed.");
                }
            }

            void OnCreatingMetadata(MetaData metadata)
            {
                MemoryProfiler.CreatingMetadata -= OnCreatingMetadata;
                metadata.Add("optifunity_version",   "1.0.0");
                metadata.Add("snapshot_type",        snapshotType.ToString());
                metadata.Add("platform_target",      Core.PlatformConfig.ActivePlatform.ToString());
                metadata.Add("android_ram_mb",       Core.PlatformConfig.AndroidPhysicalRamMB.ToString());
                metadata.Add("texture_budget_mb",    Core.PlatformConfig.TextureBudgetMB.ToString("F0"));
                metadata.Add("scene_name",           UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                metadata.Add("time_stamp",           DateTime.Now.ToString("O"));
                if (customTag != null) metadata.Add("custom_tag", customTag);
            }
#else
            Debug.LogWarning("[Optifunity] Memory Profiler chỉ hỗ trợ Unity 2021.2+");
            OnSnapshotFailed?.Invoke("Unity version not supported.");
#endif
        }

        // ─── Snapshot Path Registry ───────────────────────────────────────────

        private static void SaveSnapshotPath(SnapshotType type, string path)
        {
            EditorPrefs.SetString($"Optifunity_Snap_{type}", path);
        }

        public static string GetSnapshotPath(SnapshotType type) =>
            EditorPrefs.GetString($"Optifunity_Snap_{type}", null);

        /// <summary>Lấy danh sách tất cả file .snap trong SnapshotDirectory</summary>
        public static string[] GetAllSnapshots()
        {
            if (!Directory.Exists(SnapshotDirectory)) return Array.Empty<string>();
            return Directory.GetFiles(SnapshotDirectory, "*.snap");
        }

        /// <summary>Xóa tất cả snapshots cũ</summary>
        public static void ClearAllSnapshots()
        {
            if (!Directory.Exists(SnapshotDirectory)) return;
            foreach (var f in Directory.GetFiles(SnapshotDirectory, "*.snap"))
                File.Delete(f);
            Debug.Log("[Optifunity] Đã xóa toàn bộ memory snapshots.");
        }
    }
}
