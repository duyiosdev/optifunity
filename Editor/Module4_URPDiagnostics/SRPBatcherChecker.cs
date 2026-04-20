using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module4
{
    /// <summary>
    /// Kiểm tra tính tương thích SRP Batcher của tất cả Shaders/Materials trong project.
    /// SRP Batcher bị vô hiệu hóa khi Shader không có CBUFFER đúng chuẩn.
    /// </summary>
    public static class SRPBatcherChecker
    {
        [System.Serializable]
        public class ShaderCompatibilityInfo
        {
            public string ShaderName;
            public string ShaderPath;
            public bool   IsSRPBatcherCompatible;
            public string IncompatibilityReason;
        }

        /// <summary>
        /// Kiểm tra tất cả Shaders trong project
        /// </summary>
        public static List<PerformanceIssue> CheckAllShaders()
        {
            var issues = new List<PerformanceIssue>();

            string[] guids = AssetDatabase.FindAssets("t:Shader");
            int incompatibleCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/")) continue;

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null) continue;

                var info = CheckShader(shader, path);
                if (!info.IsSRPBatcherCompatible)
                {
                    incompatibleCount++;
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = $"Shader không tương thích SRP Batcher: '{shader.name}'",
                        Description   = $"Shader không đóng gói properties vào CBUFFER_START/CBUFFER_END block. " +
                                        $"Điều này khiến SRP Batcher không thể batch Material sử dụng shader này, " +
                                        $"buộc CPU phải gọi SetPass Call tốn kém cho mỗi draw.",
                        AssetPath     = path,
                        FixSuggestion = "Bọc tất cả properties trong shader bằng:\n" +
                                        "CBUFFER_START(UnityPerMaterial)\n" +
                                        "  // ... properties ...\n" +
                                        "CBUFFER_END\n" +
                                        "Xem tài liệu Unity: SRP Batcher → Making Your Shaders Compatible."
                    });
                }
            }

            if (incompatibleCount == 0 && guids.Length > 0)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Info,
                    Title         = $"✓ Tất cả {guids.Length} Custom Shaders tương thích SRP Batcher",
                    Description   = "Không phát hiện Shader nào vi phạm quy tắc CBUFFER.",
                    FixSuggestion = "Tiếp tục đảm bảo tính tương thích khi thêm Shader mới."
                });
            }

            // Check Dynamic Batching compatibility (mesh vertex count)
            CheckDynamicBatchingVertexLimits(issues);

            return issues;
        }

        /// <summary>
        /// Kiểm tra một Shader cụ thể
        /// </summary>
        public static ShaderCompatibilityInfo CheckShader(Shader shader, string path)
        {
            var info = new ShaderCompatibilityInfo
            {
                ShaderName = shader.name,
                ShaderPath = path
            };

            // Unity không cung cấp public API để check SRP Batcher compatibility trực tiếp.
            // Heuristic: đọc nội dung file Shader và kiểm tra CBUFFER block.
            try
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                if (System.IO.File.Exists(fullPath))
                {
                    string content = System.IO.File.ReadAllText(fullPath);
                    bool hasCbuffer = content.Contains("CBUFFER_START(UnityPerMaterial)") ||
                                      content.Contains("CBUFFER_START (UnityPerMaterial)");

                    bool hasProperties = content.Contains("Properties") &&
                                         content.Contains("SubShader");

                    // Shader Graph shaders auto-generate CBUFFER — chúng tương thích
                    bool isShaderGraph = content.Contains("// Made with Amplify Shader Editor") ||
                                         path.Contains(".shadergraph") ||
                                         content.Contains("UnityEditor.ShaderGraph");

                    info.IsSRPBatcherCompatible = isShaderGraph || hasCbuffer || !hasProperties;
                    if (!info.IsSRPBatcherCompatible)
                        info.IncompatibilityReason = "Thiếu CBUFFER_START(UnityPerMaterial) block";
                }
                else
                {
                    info.IsSRPBatcherCompatible = true; // Không đọc được → assume OK
                }
            }
            catch
            {
                info.IsSRPBatcherCompatible = true;
            }

            return info;
        }

        /// <summary>
        /// Cảnh báo về Dynamic Batching với objects có nhiều vertices
        /// </summary>
        private static void CheckDynamicBatchingVertexLimits(List<PerformanceIssue> issues)
        {
            var pipelineAsset = GraphicsSettings.renderPipelineAsset as UniversalRenderPipelineAsset;
            if (pipelineAsset == null || !pipelineAsset.supportsDynamicBatching) return;

            // Tìm meshes > 300 vertices — Dynamic Batching không hiệu quả cho chúng
            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh");
            int largeCount = 0;

            foreach (string guid in meshGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) continue;

                if (mesh.vertexCount > 300)
                {
                    largeCount++;
                    Resources.UnloadAsset(mesh);
                }
            }

            if (largeCount > 10)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Warning,
                    Title         = $"Dynamic Batching không hiệu quả: {largeCount} meshes vượt 300 vertices",
                    Description   = $"Dynamic Batching chỉ hoạt động với meshes < 300 vertices. " +
                                    $"{largeCount} meshes vượt ngưỡng này — Unity phải kiểm tra nhưng không thể batch, " +
                                    $"lãng phí CPU cycles.",
                    FixSuggestion = "Xem xét tắt Dynamic Batching trong URP Asset nếu hầu hết objects > 300 vertices. " +
                                    "Dùng SRP Batcher + GPU Instancing/Static Batching thay thế."
                });
            }
        }
    }
}
