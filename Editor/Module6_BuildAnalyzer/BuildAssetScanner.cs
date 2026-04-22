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
        }

        public static BuildResults GetBuildShadersAndMaterials()
        {
            var results = new BuildResults();
            var roots = new HashSet<string>();

            // 1. Build Scenes
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    roots.Add(scene.path);
                }
            }

            // 2. Resources Folders (Any asset in a folder named 'Resources')
            foreach (var guid in AssetDatabase.FindAssets(""))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/")) continue;
                
                if (path.Contains("/Resources/") || path.Contains("\\Resources\\"))
                {
                    roots.Add(path);
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
                                if (!string.IsNullOrEmpty(path)) roots.Add(path);
                            }
                        }
                    }
                }
            }

            // Execute dependency collection
            EditorUtility.DisplayProgressBar("Optifunity", "Scanning Build Dependencies...", 0.5f);
            
            string[] deps = AssetDatabase.GetDependencies(roots.ToArray(), true);

            foreach (var path in deps)
            {
                if (path.EndsWith(".shader") || path.EndsWith(".shadergraph") || path.EndsWith(".compute"))
                {
                    results.Shaders.Add(path);
                }
                else if (path.EndsWith(".mat"))
                {
                    results.Materials.Add(path);
                }
            }

            results.Shaders.Sort();
            results.Materials.Sort();
            results.AllDependencies = new HashSet<string>(deps);

            EditorUtility.ClearProgressBar();

            return results;
        }
    }
}
