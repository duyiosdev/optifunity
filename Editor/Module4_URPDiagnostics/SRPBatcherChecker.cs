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
            public bool?  IsSRPBatcherCompatible;
            public string IncompatibilityReason;
        }

        /// <summary>
        /// Kiểm tra tất cả Shaders trong project
        /// </summary>
        public static List<PerformanceIssue> CheckAllShaders()
        {
            var issues = new List<PerformanceIssue>();
            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                issues.Add(new PerformanceIssue { RuleId = "render.shader-compatibility", EvidenceLevel = "Advisory",
                    Severity = IssueSeverity.Info, Title = "Shader compatibility requires compiled evidence",
                    AssetPath = path, Description = "Source text, instancing buffers and generator comments do not prove SRP Batcher compatibility.",
                    FixSuggestion = "Inspect the compiled shader in Unity Inspector and verify the actual render path in Frame Debugger." });
            }
            return issues;
        }

        public static ShaderCompatibilityInfo CheckShader(Shader shader, string path) => new ShaderCompatibilityInfo
        {
            ShaderName = shader != null ? shader.name : "Unknown", ShaderPath = path,
            IsSRPBatcherCompatible = null,
            IncompatibilityReason = "Unknown: compiled shader compatibility has not been inspected."
        };

        /// <summary>
        /// Cố gắng tự động sửa Shader bằng cách bọc các thuộc tính vào CBUFFER.
        /// </summary>
        public static void TryFixShader(string assetPath)
        {
            throw new System.InvalidOperationException("Automatic shader rewriting is disabled: include resolution, compilation and rollback are not verified.");
        }
    }
}
