using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Module1;
using Optifunity.Editor.Module3;
using Optifunity.Editor.Module4;
using Optifunity.Editor.Module5;
using Optifunity.Editor.Module6;
using Optifunity.Runtime;

namespace Optifunity.Editor.UI
{
    public sealed class EvidenceWindow : EditorWindow
    {
        private bool includePackages;
        private string display = "Choose an evidence source. Missing data is not a pass.";
        private Vector2 scroll;
        private MemoryEvidence previousMemory;
        [MenuItem("Tools/Optifunity/Evidence and Benchmarks", priority = 60)]
        public static void Open() => GetWindow<EvidenceWindow>("Optifunity Evidence");
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Evidence has an explicit scope. Inventory is not build inclusion; Editor measurements are not low-end-device benchmarks. Automatic fixes remain disabled.", MessageType.Info);
            includePackages = EditorGUILayout.Toggle("Include Packages", includePackages);
            if (GUILayout.Button("Inventory and latest BuildReport")) Run(() => ProjectInventoryScanner.Scan(includePackages, Cancel));
            if (GUILayout.Button("Rendering usage: saved scenes and prefabs")) Run(() => RenderingUsageScanner.Scan(
                AssetDatabase.GetAllAssetPaths().Where(p => (p.StartsWith("Assets/") || includePackages && p.StartsWith("Packages/")) && (p.EndsWith(".unity") || p.EndsWith(".prefab"))), Cancel));
            if (GUILayout.Button("Code: helper paths and ownership"))
                Run(() => { var issues = CodeAnalysisRunner.RunAll(includeCallGraph: true); return new CodeEvidence { coverage = CodeAnalysisRunner.LastCoverage, issues = issues.ToArray() }; });
            if (GUILayout.Button("Selected Profiler frame: all captured threads")) Run(() => MultiThreadFrameReader.Read(ProfilerHelper.GetSelectedFrame()));
            if (GUILayout.Button("Editor memory: capture aggregate / compare with previous"))
                Run(() => { var current = MemoryEvidenceReader.Read(); object result = previousMemory == null ? (object)current : MemoryEvidenceReader.Compare(previousMemory, current); previousMemory = current; return result; });
            using (new EditorGUI.DisabledScope(MemoryEvidenceReader.SnapshotPending))
                if (GUILayout.Button("Capture native memory .snap (Editor)"))
                {
                    string path = EditorUtility.SaveFilePanel("New native memory snapshot", "", "Optifunity-memory", "snap");
                    if (!string.IsNullOrEmpty(path))
                    {
                        try { display = "Memory capture pending; capture can pause the Editor."; MemoryEvidenceReader.CaptureNative(path, (saved, ok) => { display = ok ? "Saved " + saved + ". Inspect/compare .snap graphs with Unity Memory Profiler; aggregate comparison above is separate." : "Memory capture failed."; Repaint(); }); }
                        catch (Exception ex) { display = ex.Message; }
                    }
                }
            if (GUILayout.Button("Open Unity Memory Profiler for native snapshot comparison"))
            {
                if (!EditorApplication.ExecuteMenuItem("Window/Analysis/Memory Profiler"))
                    display = "Install the optional com.unity.memoryprofiler package (validated fixture: 1.1.11), then open Window > Analysis > Memory Profiler. Import two .snap files and use Compare; no package is installed automatically.";
            }
            if (GUILayout.Button("Compare two device capture JSON files"))
            {
                string before = EditorUtility.OpenFilePanel("Before capture", "", "json");
                if (!string.IsNullOrEmpty(before))
                {
                    string after = EditorUtility.OpenFilePanel("After capture", "", "json");
                    if (!string.IsNullOrEmpty(after)) Run(() => DeviceCaptureAnalysis.Compare(JsonUtility.FromJson<DeviceCapture>(File.ReadAllText(before)), JsonUtility.FromJson<DeviceCapture>(File.ReadAllText(after))));
                }
            }
            if (GUILayout.Button("Export displayed evidence"))
            {
                string path = EditorUtility.SaveFilePanel("Export evidence", "", "Optifunity-evidence", "json");
                if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, display.StartsWith("{") ? display : JsonUtility.ToJson(new EvidenceStatus { status = display }, true));
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(display, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
        private static bool Cancel() => EditorUtility.DisplayCancelableProgressBar("Optifunity evidence", "Inspecting selected scope", 0);
        private void Run(Func<object> read)
        {
            try { display = JsonUtility.ToJson(read(), true); }
            catch (Exception ex) { display = "Incomplete: " + ex.Message; }
            finally { EditorUtility.ClearProgressBar(); }
        }
        [Serializable] private class EvidenceStatus { public string status; }
        [Serializable] private class CodeEvidence { public string coverage; public Core.PerformanceIssue[] issues; }
    }
}
