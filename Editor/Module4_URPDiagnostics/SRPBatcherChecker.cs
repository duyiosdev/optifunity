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
                        Title         = "Shader không tương thích SRP Batcher",
                        Description   = $"Shader '{shader.name}' không đóng gói properties vào CBUFFER_START/CBUFFER_END block. " +
                                        $"Điều này khiến SRP Batcher không thể batch Material sử dụng shader này, " +
                                        $"buộc CPU phải gọi SetPass Call tốn kém cho mỗi draw.",
                        AssetPath     = path,
                        FixSuggestion = "Bọc tất cả properties trong shader bằng:\n" +
                                        "CBUFFER_START(UnityPerMaterial)\n" +
                                        "  // ... properties ...\n" +
                                        "CBUFFER_END\n" +
                                        "Xem tài liệu Unity: SRP Batcher → Making Your Shaders Compatible.",
                        CanAutoFix    = !path.Contains(".shadergraph") && path.EndsWith(".shader"),
                        AutoFixLabel  = "Wrap in CBUFFER",
                        AutoFixAction = () => TryFixShader(path)
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
                    bool hasInstancing = content.Contains("UNITY_INSTANCING_BUFFER_START");
                    bool hasCbuffer = System.Text.RegularExpressions.Regex.IsMatch(content, @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\)") || hasInstancing;

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
            var pipelineAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
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

        /// <summary>
        /// Cố gắng tự động sửa Shader bằng cách bọc các thuộc tính vào CBUFFER.
        /// </summary>
        public static void TryFixShader(string assetPath)
        {
            string fullPath = System.IO.Path.GetFullPath(assetPath);
            if (!System.IO.File.Exists(fullPath)) return;

            try
            {
                // Create backup
                string backupPath = fullPath + ".bak";
                if (!System.IO.File.Exists(backupPath))
                    System.IO.File.Copy(fullPath, backupPath);
                string content = System.IO.File.ReadAllText(fullPath);
                
                // 0. Safeguard: Never touch shaders already using GPU Instancing (they are implicitly compatible)
                if (content.Contains("UNITY_INSTANCING_BUFFER_START"))
                {
                    Debug.Log($"[Optifunity] Shader {assetPath} uses GPU Instancing (natively SRP Batcher compatible). No Auto-Fix required.");
                    return;
                }
                
                // 1. Ultimate Property Extraction & Mapping (with Edge Case Fixes)
                string propBlockText = "";
                int propStartIndex = content.IndexOf("Properties");
                if (propStartIndex != -1)
                {
                    int endSub = content.IndexOf("SubShader", propStartIndex);
                    int endCat = content.IndexOf("Category", propStartIndex);
                    int endCG = content.IndexOf("CGINCLUDE", propStartIndex);
                    int endHLSL = content.IndexOf("HLSLINCLUDE", propStartIndex);

                    int minEnd = content.Length;
                    if (endSub != -1 && endSub < minEnd) minEnd = endSub;
                    if (endCat != -1 && endCat < minEnd) minEnd = endCat;
                    if (endCG != -1 && endCG < minEnd) minEnd = endCG;
                    if (endHLSL != -1 && endHLSL < minEnd) minEnd = endHLSL;

                    if (minEnd < content.Length)
                        propBlockText = content.Substring(propStartIndex, minEnd - propStartIndex);
                }

                if (string.IsNullOrEmpty(propBlockText)) return;

                var props = new System.Collections.Generic.Dictionary<string, string>(); // name -> expected hlsl type
                var propLines = propBlockText.Split('\n');
                
                foreach (var line in propLines)
                {
                    if (line.Trim().StartsWith("//")) continue;
                    // Match: [Attribute] _Name ("Display", Type) = ...
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"([a-zA-Z_][a-zA-Z0-9_]*)\s*\(\s*""[^""]*""\s*,\s*([a-zA-Z0-9_]+)");
                    if (m.Success)
                    {
                        string name = m.Groups[1].Value.Trim();
                        string type = m.Groups[2].Value.ToLower();

                        if (type == "color" || type == "vector") {
                            props[name] = "float4";
                        } else if (type == "range" || type == "float") {
                            props[name] = "float";
                        } else if (type == "int") {
                            props[name] = "int";
                        } else if (type.Contains("2d") || type.Contains("3d") || type.Contains("cube")) {
                            // Textures must NOT be in CBUFFER, but their ST offsets MUST be (unless NoScaleOffset is specified)
                            if (!line.Contains("[NoScaleOffset]"))
                                props[name + "_ST"] = "float4";
                        }
                    }
                }

                if (props.Count == 0) return; // Properties empty -> automatically compatible

                // 2. Process all Program and Include blocks
                var programRegex = new System.Text.RegularExpressions.Regex(@"(HLSLPROGRAM|CGPROGRAM|HLSLINCLUDE|CGINCLUDE)(.*?)(ENDHLSL|ENDCG)", System.Text.RegularExpressions.RegexOptions.Singleline);
                
                string newContent = programRegex.Replace(content, m =>
                {
                    string tagStart = m.Groups[1].Value;
                    string body     = m.Groups[2].Value;
                    string tagEnd   = m.Groups[3].Value;

                    if (System.Text.RegularExpressions.Regex.IsMatch(body, @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\)")) return m.Value;

                    var foundProps = new System.Collections.Generic.Dictionary<string, string>();

                    // Match and extract existing declarations (preserving user's specific types like half/fixed and initializers)
                    string pattern = @"(?:\b(?:uniform|static|const|inline)\s+)*\b(float|half|int|fixed|real|Vector|Color)(?:[1-4]|2x2|3x3|4x4)?\b\s+(" + string.Join("|", props.Keys) + @")\b(\s*=[^;]+)?\s*;";
                    var declRegex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Multiline);
                    
                    string modifiedBody = declRegex.Replace(body, dm =>
                    {
                        string name = dm.Groups[2].Value;
                        string cleanDecl = dm.Value.Replace("uniform", "").Trim();
                        // Handle multiple matching lines by storing the first/best one
                        if (!foundProps.ContainsKey(name)) 
                            foundProps[name] = cleanDecl;
                        return ""; // Remove from scattered positions
                    });

                    // 3. Synthesize the ultimate CBUFFER
                    string cbufferBlock = "\n#ifndef OPTIFUNITY_CBUFFER_INCLUDED\n#define OPTIFUNITY_CBUFFER_INCLUDED\n    CBUFFER_START(UnityPerMaterial)\n";
                    foreach (var kvp in props)
                    {
                        if (foundProps.TryGetValue(kvp.Key, out string userDecl))
                        {
                            cbufferBlock += "        " + userDecl + "\n";
                        }
                        else
                        {
                            // Missing property! Auto-generate it to fulfill SRP Batcher blueprint requirements!
                            cbufferBlock += $"        {kvp.Value} {kvp.Key};\n";
                        }
                    }
                    cbufferBlock += "    CBUFFER_END\n#endif\n";

                    // Smart injection
                    int lastInclude = modifiedBody.LastIndexOf("#include");
                    if (lastInclude != -1)
                    {
                        int endOfLine = modifiedBody.IndexOf('\n', lastInclude);
                        if (endOfLine != -1)
                            modifiedBody = modifiedBody.Insert(endOfLine + 1, cbufferBlock);
                        else
                            modifiedBody += cbufferBlock;
                    }
                    else
                    {
                        modifiedBody = cbufferBlock + modifiedBody;
                    }

                    return tagStart + modifiedBody + tagEnd;
                });

                if (newContent != content)
                {
                    System.IO.File.WriteAllText(fullPath, newContent);
                    AssetDatabase.ImportAsset(assetPath);
                    Debug.Log($"[Optifunity] Auto-Fixed SRP Batcher compatibility (Robust) cho: {assetPath}. Backup tại: {backupPath}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Optifunity] Lỗi khi Auto-Fix Shader {assetPath}: {e.Message}");
            }
        }

        /// <summary>
        /// Khôi phục toàn bộ shaders bị sửa lỗi thủ công từ các file .bak.
        /// </summary>
        [MenuItem("Tools/Optifunity/🚑 Restore Shader Backups")]
        public static void RestoreBackups()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:Shader");
                int count = 0;
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.StartsWith("Packages/")) continue;

                    string fullPath = System.IO.Path.GetFullPath(path);
                    string bakPath = fullPath + ".bak";

                    if (System.IO.File.Exists(bakPath))
                    {
                        System.IO.File.Copy(bakPath, fullPath, true);
                        System.IO.File.Delete(bakPath);
                        AssetDatabase.ImportAsset(path);
                        count++;
                    }
                }
                
                if (count > 0)
                    Debug.Log($"[Optifunity] Đã khôi phục thành công {count} shaders từ file .bak dự phòng!");
                else
                    Debug.Log($"[Optifunity] Không tìm thấy file .bak nào cần khôi phục.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Optifunity] Lỗi khi khôi phục shader: {e.Message}");
            }
        }
    }
}
