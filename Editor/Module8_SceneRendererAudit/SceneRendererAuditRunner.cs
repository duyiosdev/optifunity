using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module4;

namespace Optifunity.Editor.Module8
{
    public static class SceneRendererAuditRunner
    {
        public static List<PerformanceIssue> RunAll(SceneMeshRendererScanner.ScanOptions options = null)
        {
            var issues = SceneMeshRendererScanner.AnalyzeOpenScenes(options);
            foreach (var issue in issues)
                issue.Module = IssueModule.SceneRendererAudit;
            return issues;
        }

        public static bool TryLocateGameObject(PerformanceIssue issue)
        {
            if (!TryExtractSceneGameObjectPath(issue?.CodeLocation, out _, out var hierarchyPath))
                return false;

            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                var target = FindByHierarchyPath(scene, hierarchyPath);
                if (target == null) continue;

                Selection.activeObject = target;
                EditorGUIUtility.PingObject(target);
                return true;
            }

            return false;
        }

        public static bool TryLocateReferencingLodGroup(PerformanceIssue issue)
        {
            if (!TryExtractSceneGameObjectPath(issue?.CodeLocation, out _, out var hierarchyPath))
                return false;

            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                var target = FindByHierarchyPath(scene, hierarchyPath);
                if (target == null) continue;

                var lodGroup = FindReferencingLodGroup(target);
                if (lodGroup == null) return false;

                Selection.activeObject = lodGroup;
                EditorGUIUtility.PingObject(lodGroup);
                return true;
            }

            return false;
        }

        public static bool TryExtractSceneGameObjectPath(string codeLocation, out string sceneName, out string hierarchyPath)
        {
            sceneName = null;
            hierarchyPath = null;

            if (string.IsNullOrEmpty(codeLocation)) return false;

            const string prefix = "scenego://";
            if (!codeLocation.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string payload = codeLocation.Substring(prefix.Length);
            int split = payload.IndexOf('|');
            if (split <= 0 || split >= payload.Length - 1) return false;

            sceneName = payload.Substring(0, split);
            hierarchyPath = payload.Substring(split + 1);
            return !string.IsNullOrEmpty(sceneName) && !string.IsNullOrEmpty(hierarchyPath);
        }

        private static GameObject FindByHierarchyPath(Scene scene, string hierarchyPath)
        {
            string[] parts = hierarchyPath.Split('/');
            if (parts.Length == 0) return null;

            GameObject current = null;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == parts[0])
                {
                    current = roots[i];
                    break;
                }
            }

            if (current == null) return null;

            for (int i = 1; i < parts.Length; i++)
            {
                var child = current.transform.Find(parts[i]);
                if (child == null) return null;
                current = child.gameObject;
            }

            return current;
        }

        private static LODGroup FindReferencingLodGroup(GameObject target)
        {
            if (target == null) return null;

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return null;

            var groups = target.GetComponentsInParent<LODGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                var lods = groups[i].GetLODs();
                for (int l = 0; l < lods.Length; l++)
                {
                    var lodRenderers = lods[l].renderers;
                    for (int r = 0; r < lodRenderers.Length; r++)
                    {
                        if (lodRenderers[r] == renderer)
                            return groups[i];
                    }
                }
            }

            return null;
        }
    }
}
