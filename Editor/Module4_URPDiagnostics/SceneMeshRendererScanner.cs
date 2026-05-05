using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module4
{
    public static class SceneMeshRendererScanner
    {
        [Serializable]
        public class ScanOptions
        {
            public bool IncludeInactive = true;
            public bool IncludeSkinnedMeshRenderers = false;
            public int RepeatedRendererThreshold = 3;
            public int HeavyMeshVertexThreshold = 20000;
            public float HeavyBoundsMagnitudeThreshold = 25f;
            public bool EmitPerRendererIssues = true;

            public bool EnableGpuInstancingFilter = false;
            public bool GpuInstancingExpectedOn = true;

            public bool EnableMaterialPropertyBlockFilter = false;
            public bool MaterialPropertyBlockExpectedOn = true;

            public bool EnableStaticBatchingFilter = false;
            public bool StaticBatchingExpectedOn = true;

            public bool EnableLodGroupFilter = false;
            public bool LodGroupExpectedOn = true;

            public bool EnableXrMotionFilter = false;
            public bool XrMotionExpectedOn = true;

            public bool EnableDotsInstancingFilter = false;
            public bool DotsInstancingExpectedOn = true;
        }

        public static List<PerformanceIssue> AnalyzeOpenScenes(ScanOptions options = null)
        {
            options ??= new ScanOptions();

            var issues = new List<PerformanceIssue>();
            var renderers = EnumerateOpenSceneRenderers(options.IncludeInactive, options.IncludeSkinnedMeshRenderers).ToList();

            if (renderers.Count == 0)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Info,
                    Title = "Open Scene Renderer Scan",
                    Description = "Không tìm thấy renderer nào trong các scene đang mở theo cấu hình hiện tại.",
                    FixSuggestion = "Mở scene gameplay hoặc bật thêm tùy chọn scan (ví dụ Skinned Mesh Renderer) rồi scan lại."
                });
                return issues;
            }

            bool hasAnyActiveFilter =
                options.EnableMaterialPropertyBlockFilter ||
                options.EnableGpuInstancingFilter ||
                options.EnableStaticBatchingFilter ||
                options.EnableLodGroupFilter ||
                options.EnableXrMotionFilter ||
                options.EnableDotsInstancingFilter;

            if (!hasAnyActiveFilter)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity = IssueSeverity.Info,
                    Title = "Scene filter chưa được chọn",
                    Description = "Hãy tick ít nhất một filter tiêu chí (MPB/Instancing/Static/LOD/XR/DOTS) để nhận danh sách kết quả lọc.",
                    FixSuggestion = "Checkbox 'Scan Skinned Mesh Renderer' chỉ mở rộng phạm vi quét, không phải điều kiện lọc kết quả."
                });
                return issues;
            }

            foreach (var renderer in renderers)
            {
                var scenePath = renderer.gameObject.scene.path;
                if (string.IsNullOrEmpty(scenePath)) scenePath = $"Scene:{renderer.gameObject.scene.name}";

                bool hasMpb = HasMaterialPropertyBlock(renderer);

                var mats = renderer.sharedMaterials;
                bool hasInstancingMaterial = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].enableInstancing)
                    {
                        hasInstancingMaterial = true;
                        break;
                    }
                }

                bool isStaticBatched = renderer.isPartOfStaticBatch;

                bool inLodGroup = IsRendererReferencedByAnyLodGroup(renderer);

                var xrState = GetXrMotionState(mats);

                bool supportsDotsInstancing = SupportsDotsInstancing(mats);

                bool includeRenderer =
                    MatchesExpected(options.EnableMaterialPropertyBlockFilter, hasMpb, options.MaterialPropertyBlockExpectedOn) &&
                    MatchesExpected(options.EnableGpuInstancingFilter, hasInstancingMaterial, options.GpuInstancingExpectedOn) &&
                    MatchesExpected(options.EnableStaticBatchingFilter, isStaticBatched, options.StaticBatchingExpectedOn) &&
                    MatchesExpected(options.EnableLodGroupFilter, inLodGroup, options.LodGroupExpectedOn) &&
                    MatchesExpected(options.EnableDotsInstancingFilter, supportsDotsInstancing, options.DotsInstancingExpectedOn) &&
                    MatchesExpected(options.EnableXrMotionFilter, xrState, options.XrMotionExpectedOn);

                if (!includeRenderer)
                    continue;

                if (options.EmitPerRendererIssues)
                {
                    if (options.EnableMaterialPropertyBlockFilter)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = hasMpb ? IssueSeverity.Info : IssueSeverity.Warning,
                            Title = hasMpb ? "MaterialPropertyBlock = On" : "MaterialPropertyBlock = Off",
                            AssetPath = scenePath,
                            CodeLocation = BuildSceneObjectToken(renderer),
                            Description = $"{BuildHierarchyPath(renderer.transform)} có MaterialPropertyBlock = {(hasMpb ? "On" : "Off")}.",
                            FixSuggestion = "Dùng MPB khi cần override per-renderer; giữ Off khi không cần để giảm complexity."
                        });
                    }
                    else if (hasMpb)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = IssueSeverity.Info,
                            Title = "MaterialPropertyBlock detected on renderer",
                            AssetPath = scenePath,
                            CodeLocation = BuildSceneObjectToken(renderer),
                            Description = $"{BuildHierarchyPath(renderer.transform)} đang có MaterialPropertyBlock tại thời điểm scan (snapshot).",
                            FixSuggestion = "Dùng MPB cho override per-renderer có chủ đích và tránh tạo material instance không cần thiết."
                        });
                    }

                    if (options.EnableGpuInstancingFilter)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = hasInstancingMaterial ? IssueSeverity.Info : IssueSeverity.Warning,
                            Title = hasInstancingMaterial ? "GPU Instancing = On" : "GPU Instancing = Off",
                            AssetPath = scenePath,
                            CodeLocation = BuildSceneObjectToken(renderer),
                            Description = $"{BuildHierarchyPath(renderer.transform)} có GPU Instancing = {(hasInstancingMaterial ? "On" : "Off")} theo material hiện tại.",
                            FixSuggestion = "Bật Enable Instancing cho material khi object lặp nhiều; giữ Off nếu không có lợi ích thực tế khi profile."
                        });
                    }

                    if (options.EnableStaticBatchingFilter)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = isStaticBatched ? IssueSeverity.Info : IssueSeverity.Warning,
                            Title = isStaticBatched ? "Static Batching = On" : "Static Batching = Off",
                            AssetPath = scenePath,
                            CodeLocation = BuildSceneObjectToken(renderer),
                            Description = $"{BuildHierarchyPath(renderer.transform)} có trạng thái static batching = {(isStaticBatched ? "On" : "Off")} tại thời điểm scan.",
                            FixSuggestion = "Đối chiếu workflow scene với static batching setting và xác nhận hiệu quả bằng Profiler."
                        });
                    }

                    if (options.EnableLodGroupFilter)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = inLodGroup ? IssueSeverity.Info : IssueSeverity.Warning,
                            Title = inLodGroup ? "LODGroup = On" : "LODGroup = Off",
                            AssetPath = scenePath,
                            CodeLocation = BuildSceneObjectToken(renderer),
                            Description = $"{BuildHierarchyPath(renderer.transform)} có LODGroup = {(inLodGroup ? "On" : "Off")}.",
                            FixSuggestion = "Bổ sung LODGroup cho object nặng hoặc object ở xa camera để giảm triangle cost."
                        });
                    }

                    if (options.EnableXrMotionFilter)
                    {
                        string xrLabel = xrState == XrMotionState.Enabled ? "On" : xrState == XrMotionState.Disabled ? "Off" : "Unknown";
                        issues.Add(new PerformanceIssue
                        {
                            Severity = xrState == XrMotionState.Enabled ? IssueSeverity.Info : IssueSeverity.Warning,
                            Title = $"XR Motion = {xrLabel}",
                            AssetPath = scenePath,
                            CodeLocation = BuildSceneObjectToken(renderer),
                            Description = $"{BuildHierarchyPath(renderer.transform)} có XR Motion = {xrLabel} theo material/keyword hiện đọc được.",
                            FixSuggestion = "Đặt XR Motion phù hợp với yêu cầu gameplay/render path XR của object này."
                        });
                    }

                    if (options.EnableDotsInstancingFilter)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = supportsDotsInstancing ? IssueSeverity.Info : IssueSeverity.Warning,
                            Title = supportsDotsInstancing ? "DOTS Instancing Support = On" : "DOTS Instancing Support = Off",
                            AssetPath = scenePath,
                            CodeLocation = BuildSceneObjectToken(renderer),
                            Description = $"{BuildHierarchyPath(renderer.transform)} có shader/material DOTS Instancing Support = {(supportsDotsInstancing ? "On" : "Off")} theo keyword/property khả dụng.",
                            FixSuggestion = "Bật/tối ưu DOTS Instancing trên shader phù hợp nếu pipeline của project cần tận dụng render path này."
                        });
                    }

                }

            }


            return issues;
        }

        private static IEnumerable<Renderer> EnumerateOpenSceneRenderers(bool includeInactive, bool includeSkinnedMeshRenderers)
        {
            var result = new List<Renderer>();
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    result.AddRange(roots[r].GetComponentsInChildren<MeshRenderer>(includeInactive));
                    if (includeSkinnedMeshRenderers)
                        result.AddRange(roots[r].GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive));
                }
            }
            return result;
        }

        private static bool HasMaterialPropertyBlock(Renderer renderer)
        {
            try
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                return !block.isEmpty;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsHeavyRenderer(Renderer renderer, ScanOptions options)
        {
            int vertexCount = 0;
            if (renderer is MeshRenderer meshRenderer)
            {
                var filter = meshRenderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    vertexCount = filter.sharedMesh.vertexCount;
            }
            else if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                vertexCount = skinned.sharedMesh.vertexCount;
            }

            float boundsMagnitude = renderer.bounds.size.magnitude;
            return vertexCount >= options.HeavyMeshVertexThreshold || boundsMagnitude >= options.HeavyBoundsMagnitudeThreshold;
        }

        private static bool IsRendererReferencedByAnyLodGroup(Renderer renderer)
        {
            if (renderer == null) return false;

            var lodGroups = renderer.GetComponentsInParent<LODGroup>(true);
            for (int i = 0; i < lodGroups.Length; i++)
            {
                var lods = lodGroups[i].GetLODs();
                for (int l = 0; l < lods.Length; l++)
                {
                    var lodRenderers = lods[l].renderers;
                    for (int r = 0; r < lodRenderers.Length; r++)
                    {
                        if (lodRenderers[r] == renderer)
                            return true;
                    }
                }
            }

            return false;
        }

        private enum XrMotionState
        {
            Unknown,
            Enabled,
            Disabled
        }

        private static XrMotionState GetXrMotionState(Material[] materials)
        {
            if (materials == null || materials.Length == 0) return XrMotionState.Unknown;

            for (int i = 0; i < materials.Length; i++)
            {
                var mat = materials[i];
                if (mat == null) continue;

                if (TryGetXrMotionFromFloat(mat, "_XRMotion", out var xrMotion))
                    return xrMotion > 0.5f ? XrMotionState.Enabled : XrMotionState.Disabled;

                if (TryGetXrMotionFromFloat(mat, "_XR_MOTION", out var xrMotionAlt))
                    return xrMotionAlt > 0.5f ? XrMotionState.Enabled : XrMotionState.Disabled;

                if (TryGetXrMotionFromFloat(mat, "_MotionVector", out var motionVector))
                    return motionVector > 0.5f ? XrMotionState.Enabled : XrMotionState.Disabled;

                if (TryGetXrMotionFromFloat(mat, "_MotionVectors", out var motionVectors))
                    return motionVectors > 0.5f ? XrMotionState.Enabled : XrMotionState.Disabled;

                if (TryGetXrMotionFromFloat(mat, "_XRMotionVectorsPass", out var xrMotionVectorsPass))
                    return xrMotionVectorsPass > 0.5f ? XrMotionState.Enabled : XrMotionState.Disabled;

                if (TryGetXrMotionFromFloat(mat, "_ADD_PRECOMPUTED_VELOCITY", out var addPrecomputedVelocity))
                    return addPrecomputedVelocity > 0.5f ? XrMotionState.Enabled : XrMotionState.Disabled;

                if (mat.IsKeywordEnabled("XR_MOTION_ON") || mat.IsKeywordEnabled("_XR_MOTION_ON") || mat.IsKeywordEnabled("MOTION_VECTOR_ON") || mat.IsKeywordEnabled("_ADD_PRECOMPUTED_VELOCITY"))
                    return XrMotionState.Enabled;

                if (mat.IsKeywordEnabled("XR_MOTION_OFF") || mat.IsKeywordEnabled("_XR_MOTION_OFF") || mat.IsKeywordEnabled("MOTION_VECTOR_OFF"))
                    return XrMotionState.Disabled;

                return XrMotionState.Disabled;
            }

            return XrMotionState.Unknown;
        }

        private static bool SupportsDotsInstancing(Material[] materials)
        {
            if (materials == null || materials.Length == 0) return false;

            for (int i = 0; i < materials.Length; i++)
            {
                var mat = materials[i];
                if (mat == null) continue;

                if (mat.IsKeywordEnabled("DOTS_INSTANCING_ON") || mat.IsKeywordEnabled("_DOTS_INSTANCING_ON"))
                    return true;

                if (TryGetFloatProperty(mat, "_DOTSInstancing", out var dotsInstancingFlag) && dotsInstancingFlag > 0.5f)
                    return true;

                var shader = mat.shader;
                if (shader == null) continue;

                string shaderName = shader.name ?? string.Empty;
                if (shaderName.StartsWith("Universal Render Pipeline/", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (shaderName.IndexOf("DOTS", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (ShaderKeywordExists(shader, "DOTS_INSTANCING_ON") || ShaderKeywordExists(shader, "_DOTS_INSTANCING_ON"))
                    return true;
            }

            return false;
        }

        private static bool TryGetXrMotionFromFloat(Material mat, string propertyName, out float value)
        {
            value = 0f;
            if (mat == null || !mat.HasProperty(propertyName)) return false;
            value = mat.GetFloat(propertyName);
            return true;
        }

        private static bool TryGetFloatProperty(Material mat, string propertyName, out float value)
        {
            value = 0f;
            if (mat == null || !mat.HasProperty(propertyName)) return false;
            value = mat.GetFloat(propertyName);
            return true;
        }

        private static bool ShaderKeywordExists(Shader shader, string keyword)
        {
            if (shader == null || string.IsNullOrEmpty(keyword)) return false;

            var keywords = shader.keywordSpace.keywords;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (string.Equals(keywords[i].name, keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool MatchesExpected(bool filterEnabled, bool actualValue, bool expectedOn)
        {
            if (!filterEnabled) return true;
            return expectedOn ? actualValue : !actualValue;
        }

        private static bool MatchesExpected(bool filterEnabled, XrMotionState actualState, bool expectedOn)
        {
            if (!filterEnabled) return true;
            return expectedOn ? actualState == XrMotionState.Enabled : actualState == XrMotionState.Disabled;
        }

        private static string BuildHierarchyPath(Transform t)
        {
            if (t == null) return "Unknown";
            var stack = new Stack<string>();
            var current = t;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack);
        }

        private static string BuildSceneObjectToken(Renderer renderer)
        {
            if (renderer == null) return null;
            var scene = renderer.gameObject.scene;
            if (!scene.IsValid()) return null;
            return $"scenego://{scene.name}|{BuildHierarchyPath(renderer.transform)}";
        }
    }
}
