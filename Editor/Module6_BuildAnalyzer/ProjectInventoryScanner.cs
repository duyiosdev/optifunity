using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Optifunity.Editor.Module6
{
    [Serializable] public class InventoryAsset
    {
        public string path, guid, type;
        public long localId;
        public string[] dependencies;
    }
    [Serializable] public class PackedEvidence
    {
        public string path, file;
        public ulong bytes;
    }
    [Serializable] public class ProjectInventory
    {
        public string capturedAt, unityVersion, projectPath, scope, completion, addressablesStatus, buildStatus;
        public string buildTarget, buildTime;
        public List<InventoryAsset> assets = new();
        public List<string> addressableRoots = new();
        public List<PackedEvidence> packedAssets = new();
        public List<string> errors = new();
    }
    public static class ProjectInventoryScanner
    {
        public static ProjectInventory Scan(bool includePackages, Func<bool> cancel = null, IEnumerable<string> selectedPaths = null)
        {
            var result = new ProjectInventory {
                capturedAt = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                projectPath = System.IO.Path.GetFullPath("."),
                scope = (selectedPaths == null ? "Assets" + (includePackages ? " + Packages" : "") : "Explicit selected paths") + "; imported main/sub-assets and direct dependencies; disk inventory is not build inclusion",
                completion = "Complete within selected scope"
            };
            var paths = selectedPaths ?? AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") || (includePackages && p.StartsWith("Packages/")));
            foreach (var path in paths.Distinct().OrderBy(p => p))
            {
                if (cancel?.Invoke() == true) { result.completion = "Incomplete: canceled"; break; }
                if (AssetDatabase.IsValidFolder(path)) continue;
                try
                {
                    string[] deps = AssetDatabase.GetDependencies(path, false).Where(p => p != path).ToArray();
                    var objects = AssetDatabase.LoadAllAssetsAtPath(path).Where(o => o != null).ToArray();
                    if (objects.Length == 0)
                        result.assets.Add(new InventoryAsset { path = path, guid = AssetDatabase.AssetPathToGUID(path), type = "No imported object", dependencies = deps });
                    foreach (var obj in objects)
                    {
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long id);
                        result.assets.Add(new InventoryAsset { path = path, guid = guid, localId = id, type = obj.GetType().FullName, dependencies = deps });
                    }
                }
                catch (Exception ex) { result.errors.Add(path + ": " + ex.Message); result.completion = "Incomplete: asset read errors"; }
            }
            ReadAddressables(result);
            ReadBuildReport(result, BuildReport.GetLatestReport());
            return result;
        }
        private static void ReadAddressables(ProjectInventory result)
        {
            var type = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor");
            if (type == null) { result.addressablesStatus = "Not applicable: Addressables Editor package not installed"; return; }
            try
            {
                // Reading the default-settings getter can migrate legacy assets, so enumerate persisted settings without invoking it.
                var settingsPaths = AssetDatabase.FindAssets("t:AddressableAssetSettings", new[] { "Assets" });
                if (settingsPaths.Length == 0) { result.addressablesStatus = "Unknown: no persisted Addressables settings"; return; }
                foreach (var guid in settingsPaths)
                {
                    var settings = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), type);
                    if (settings == null) throw new InvalidOperationException("Unreadable Addressables settings");
                    var groups = settings.GetType().GetProperty("groups")?.GetValue(settings) as IEnumerable;
                    if (groups == null) throw new InvalidOperationException("Unsupported groups API");
                    foreach (var group in groups)
                    {
                        if (group == null) continue;
                        var entries = group.GetType().GetProperty("entries")?.GetValue(group) as IEnumerable;
                        if (entries == null) throw new InvalidOperationException("Unsupported entries API");
                        foreach (var entry in entries)
                        {
                            var path = entry.GetType().GetProperty("AssetPath")?.GetValue(entry) as string;
                            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Unreadable Addressables entry path");
                            result.addressableRoots.Add(path);
                        }
                    }
                }
                result.addressableRoots = result.addressableRoots.Distinct().OrderBy(p => p).ToList();
                result.addressablesStatus = "Configured roots from all persisted settings, including folder roots; not expanded bundle layout, remote content or proof of inclusion";
            }
            catch (Exception ex) { result.addressablesStatus = "Unsupported/partial Addressables adapter: " + ex.Message; }
        }
        public static void ReadBuildReport(ProjectInventory result, BuildReport report)
        {
            result.packedAssets.Clear();
            if (report == null) { result.buildStatus = "Unknown: no BuildReport; inventory does not prove build inclusion"; return; }
            result.buildTarget = report.summary.platform.ToString();
            result.buildTime = report.summary.buildStartedAt.ToString("o");
            result.buildStatus = report.summary.result + "; latest report may predate current assets; packed entries are serialized sizes, not runtime memory";
            foreach (var packed in report.packedAssets ?? Array.Empty<PackedAssets>())
                foreach (var item in packed.contents)
                    result.packedAssets.Add(new PackedEvidence { path = item.sourceAssetPath, file = packed.shortPath, bytes = item.packedSize });
            if (result.packedAssets.Count == 0) result.buildStatus += "; no packed detail: use DetailedBuildReport when building";
        }
    }
}
