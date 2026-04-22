using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module2
{
    /// <summary>
    /// Kiểm toán cấu hình Mesh: Read/Write flag, Animation Compression, LOD presence.
    /// </summary>
    public static class MeshAuditor
    {
        public static List<PerformanceIssue> Audit(bool myScriptsOnly = false)
        {
            var issues = new List<PerformanceIssue>();
            bool isMobile = PlatformConfig.IsMobile;

            string[] guids = AssetDatabase.FindAssets("t:Model");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/")) continue;
                if (myScriptsOnly && !UI.DashboardWindow.IsUserCodePath(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                // ─── 1. Read/Write Enabled ─────────────────────────────────────
                if (importer.isReadable)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = "Mesh: Read/Write Enabled — RAM bị nhân đôi",
                        Description   = $"'{System.IO.Path.GetFileName(path)}' có Read/Write Enabled = true. " +
                                        $"Unity phải duy trì hai bản sao mesh data: một trong RAM (CPU) và một trong VRAM (GPU). " +
                                        $"Điều này ngay lập tức tăng ~50% lượng RAM cho mesh này.",
                        AssetPath     = path,
                        FixSuggestion = "Tắt Read/Write Enabled trong ModelImporter nếu không cần chỉnh sửa mesh tại runtime " +
                                        "(Mesh Deformation, Mesh.vertices trực tiếp trong code).",
                        CanAutoFix    = true,
                        AutoFixLabel  = "Disable Read/Write",
                        AutoFixAction = () => AutoFixReadWrite(path)
                    });
                }

                // ─── 2. Animation Compression ──────────────────────────────────
                if (importer.importAnimation &&
                    importer.animationCompression == ModelImporterAnimationCompression.Off)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = "Animation Compression tắt — tăng dung lượng dữ liệu hoạt hình",
                        Description   = $"Model '{System.IO.Path.GetFileName(path)}' import animation nhưng không bật " +
                                        $"Animation Compression. Keyframe data chưa được tối ưu hóa, tốn RAM không cần thiết.",
                        AssetPath     = path,
                        FixSuggestion = "Bật Anim. Compression = Optimal trong ModelImporter. Unity sẽ tự động " +
                                        "loại bỏ keyframe dư thừa bằng nội suy mà không làm thay đổi đáng kể về hình ảnh.",
                        CanAutoFix    = true,
                        AutoFixLabel  = "Enable Anim Compression",
                        AutoFixAction = () => AutoFixAnimCompression(path)
                    });
                }

                // ─── 3. LOD Group presence cho mobile ──────────────────────────
                if (isMobile && importer.importBlendShapes)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Info,
                        Title         = "Model có BlendShapes — cân nhắc LOD setup cho mobile",
                        Description   = $"'{System.IO.Path.GetFileName(path)}' có BlendShapes. Trên mobile, " +
                                        $"character phức tạp không có LOD sẽ tiêu tốn GPU đáng kể khi nhiều instance trên màn hình.",
                        AssetPath     = path,
                        FixSuggestion = "Thiết lập LOD Group trên prefab: LOD0 (full detail, gần camera), " +
                                        "LOD1 (50% poly, trung bình), LOD2 (25% poly, xa). " +
                                        "Mục tiêu: tổng poly count 1-2 triệu trên mobile."
                    });
                }

                // ─── 4. Mesh Compression ───────────────────────────────────────
                if (importer.meshCompression == ModelImporterMeshCompression.Off && isMobile)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Info,
                        Title         = "Mesh Compression tắt trên Mobile target",
                        Description   = $"Bật Mesh Compression giúp giảm dung lượng file build, " +
                                        $"ít ảnh hưởng đến chất lượng hình ảnh.",
                        AssetPath     = path,
                        FixSuggestion = "Trong ModelImporter → Model tab → Mesh Compression: chọn 'Low' hoặc 'Medium'.",
                        CanAutoFix    = true,
                        AutoFixLabel  = "Set Mesh Compression = Low",
                        AutoFixAction = () => AutoFixMeshCompression(path)
                    });
                }
            }

            return issues;
        }

        private static void AutoFixReadWrite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        private static void AutoFixAnimCompression(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.SaveAndReimport();
        }

        private static void AutoFixMeshCompression(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;
            importer.meshCompression = ModelImporterMeshCompression.Low;
            importer.SaveAndReimport();
        }
    }
}
