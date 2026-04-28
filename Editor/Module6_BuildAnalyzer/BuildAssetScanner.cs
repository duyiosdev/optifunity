using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Optifunity.Editor.Module6
{
    public static class BuildAssetScanner
    {
        public class BuildResults
        {
            public List<string> Shaders = new List<string>();
            public List<string> Materials = new List<string>();
            public HashSet<string> AllDependencies = new HashSet<string>();
            public Dictionary<string, string> ShaderSources = new Dictionary<string, string>();
            public Dictionary<string, string> MaterialSources = new Dictionary<string, string>();
        }

        private static bool IsEditorPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string normalized = path.Replace('\\', '/');
            return normalized.Contains("/Editor/") || normalized.StartsWith("Editor/");
        }

        public static BuildResults GetBuildShadersAndMaterials()
        {
            var results = new BuildResults();
            var roots = new HashSet<string>();
            var rootSources = new Dictionary<string, string>();

            // 1. Build Scenes
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    roots.Add(scene.path);
                    rootSources[scene.path] = "Scene";
                }
            }

            // 2. Resources Folders (exclude Editor-only resources)
            foreach (var guid in AssetDatabase.FindAssets(""))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/")) continue;
                if (IsEditorPath(path)) continue;

                string normalized = path.Replace('\\', '/');
                if (normalized.Contains("/Resources/"))
                {
                    roots.Add(path);
                    if (!rootSources.ContainsKey(path)) rootSources[path] = "Resources";
                }
            }

            // 3. Always Included Shaders
            var graphicsSettingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettingsAssets != null && graphicsSettingsAssets.Length > 0)
            {
                var graphicsSettings = graphicsSettingsAssets[0];
                if (graphicsSettings != null)
                {
                    SerializedObject so = new SerializedObject(graphicsSettings);
                    SerializedProperty prop = so.FindProperty("m_AlwaysIncludedShaders");
                    if (prop != null)
                    {
                        for (int i = 0; i < prop.arraySize; i++)
                        {
                            var shaderProp = prop.GetArrayElementAtIndex(i);
                            if (shaderProp.objectReferenceValue != null)
                            {
                                string path = AssetDatabase.GetAssetPath(shaderProp.objectReferenceValue);
                                if (!string.IsNullOrEmpty(path))
                                {
                                    roots.Add(path);
                                    rootSources[path] = "Always Included Shader";
                                }
                            }
                        }
                    }
                }
            }

            // Execute dependency collection
            EditorUtility.DisplayProgressBar("Optifunity", "Scanning Build Dependencies...", 0.5f);
            
            string[] deps = AssetDatabase.GetDependencies(roots.ToArray(), true);

            var shaderSet = new HashSet<string>();
            var materialSet = new HashSet<string>();
            var filteredDeps = new HashSet<string>();
            var shaderSources = new Dictionary<string, string>();
            var materialSources = new Dictionary<string, string>();

            foreach (var path in deps)
            {
                if (IsEditorPath(path)) continue;

                filteredDeps.Add(path);

                string source = "Dependency";
                foreach (var kv in rootSources)
                {
                    if (path == kv.Key)
                    {
                        source = kv.Value;
                        break;
                    }
                }

                if (path.EndsWith(".shader") || path.EndsWith(".shadergraph") || path.EndsWith(".compute"))
                {
                    shaderSet.Add(path);
                    if (!shaderSources.ContainsKey(path)) shaderSources[path] = source;
                }
                else if (path.EndsWith(".mat"))
                {
                    materialSet.Add(path);
                    if (!materialSources.ContainsKey(path)) materialSources[path] = source;
                }
            }

            results.Shaders = shaderSet.OrderBy(p => p).ToList();
            results.Materials = materialSet.OrderBy(p => p).ToList();
            results.AllDependencies = filteredDeps;
            results.ShaderSources = shaderSources;
            results.MaterialSources = materialSources;

            EditorUtility.ClearProgressBar();

            return results;
        }
    }
}
