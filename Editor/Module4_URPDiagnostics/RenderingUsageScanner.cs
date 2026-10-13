using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Optifunity.Editor.Module4
{
    [Serializable] public class RenderingUsage
    {
        public string assetPath, objectPath, kind, details;
    }
    [Serializable] public class RenderingUsageReport
    {
        public string scope = "Explicit scene/prefab paths; serialized configuration includes inactive objects, not measured runtime rendering";
        public string completion = "Complete within selected scope";
        public List<RenderingUsage> usages = new();
        public List<string> errors = new();
    }
    public static class RenderingUsageScanner
    {
        public static RenderingUsageReport Scan(IEnumerable<string> paths, Func<bool> cancel = null)
        {
            var report = new RenderingUsageReport();
            foreach (var path in paths.Distinct())
            {
                if (cancel?.Invoke() == true) { report.completion = "Incomplete: canceled"; break; }
                try
                {
                    if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        var root = PrefabUtility.LoadPrefabContents(path);
                        try { Inspect(root, path, report); }
                        finally { PrefabUtility.UnloadPrefabContents(root); }
                    }
                    else if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        var scene = EditorSceneManager.OpenPreviewScene(path);
                        try { foreach (var root in scene.GetRootGameObjects()) Inspect(root, path, report); }
                        finally { EditorSceneManager.ClosePreviewScene(scene); }
                    }
                }
                catch (Exception ex) { report.errors.Add(path + ": " + ex.Message); report.completion = "Incomplete: read errors"; }
            }
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                var property = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
                if (property == null) report.errors.Add("Unknown: renderer data list adapter unavailable");
                else for (int i = 0; i < property.arraySize; i++)
                {
                    var data = property.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                    if (data == null) continue;
                    foreach (var feature in data.rendererFeatures.Where(f => f != null))
                        report.usages.Add(new RenderingUsage { assetPath = AssetDatabase.GetAssetPath(data), objectPath = pipeline.name,
                            kind = "RendererFeature", details = feature.GetType().FullName + "; active=" + feature.isActive + "; configured, camera execution not proven" });
                }
            }
            return report;
        }
        public static void Inspect(GameObject root, string path, RenderingUsageReport report)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                string details = null;
                if (component is Camera camera) details = "enabled=" + camera.enabled + "; cullingMask=" + camera.cullingMask + "; target=" + (camera.targetTexture != null ? camera.targetTexture.name : "screen");
                else if (component is Canvas canvas) details = "mode=" + canvas.renderMode + "; nested=" + !canvas.isRootCanvas;
                else if (component is CanvasRenderer ui) details = "materials=" + ui.materialCount + "; runtime rebuild/overdraw unmeasured";
                else if (component is ParticleSystem particles) details = "maxParticles=" + particles.main.maxParticles + "; collision=" + particles.collision.enabled + "; serialized limit, not live count";
                else if (component is Renderer renderer)
                    details = string.Join(" | ", renderer.sharedMaterials.Where(m => m != null).Select(m =>
                        "material=" + AssetDatabase.GetAssetPath(m) + "; shader=" + (m.shader != null ? m.shader.name : "missing") +
                        "; keywords=" + string.Join(",", m.shaderKeywords.OrderBy(k => k)))) + "; configured keywords are not final compiled variants";
                if (details == null) continue;
                var names = new List<string>();
                for (var transform = component.transform; transform != null; transform = transform.parent) names.Add(transform.name + "[" + transform.GetSiblingIndex() + "]");
                names.Reverse();
                report.usages.Add(new RenderingUsage { assetPath = path, objectPath = string.Join("/", names), kind = component.GetType().Name, details = details });
            }
        }
    }
}
