using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.UI
{
    public sealed class OptimizationWindow : EditorWindow
    {
        private Vector2 scroll;
        private string status, reason = "", selectedKey, platform = "Android";
        private int cost = 2, impact, maxSize = 512;
        private bool suppressed, reviewed;
        private Texture2D texture;
        private TextureChange preview;
        [MenuItem("Tools/Optifunity/Optimization Review", priority = 65)]
        public static void Open() => GetWindow<OptimizationWindow>("Optimization Review");
        private void Run(Action action) { try { action(); } catch (Exception ex) { status = ex.Message; } }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Priorities are review order, not predicted FPS. Suppression does not delete findings. Texture changes require explicit preview approval; other automatic fixes and MCP writes remain disabled.", MessageType.Info);
            if (GUILayout.Button("Set baseline from current scoped report")) Run(() => { OptimizationReviewStore.SetBaseline(ReportEngine.LastReport); status = "Baseline saved. Absent entries are not proven fixed."; });
            if (GUILayout.Button("Export AI context with sources")) Run(() => { string p = EditorUtility.SaveFilePanel("Assistant context", "", "Optifunity-assistant", "json"); if (!string.IsNullOrEmpty(p)) File.WriteAllText(p, JsonUtility.ToJson(AssistantEvidence.Read(), true)); });
            if (GUILayout.Button("Clear shared session evidence")) { AssistantEvidence.Clear(); status = "Shared capture cache cleared."; }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            try
            {
                var report = OptimizationReviewStore.Review(ReportEngine.LastReport);
                EditorGUILayout.LabelField(report.completion, EditorStyles.wordWrappedLabel);
                foreach (var entry in report.findings)
                    if (GUILayout.Button((entry.suppressed ? "[Suppressed] " : "") + entry.finding.Title + " — " + entry.change))
                    { selectedKey = entry.key; reason = entry.reason ?? ""; suppressed = entry.suppressed; cost = entry.cost; impact = entry.impact; status = entry.priorityReason + "\n" + entry.rule.recommendation + "\n" + string.Join("\n", entry.rule.sources); }
                if (!string.IsNullOrEmpty(selectedKey))
                {
                    suppressed = EditorGUILayout.Toggle("Suppress (keep visible)", suppressed);
                    reason = EditorGUILayout.TextField("Reason", reason);
                    cost = EditorGUILayout.IntSlider("Estimated cost", cost, 1, 3);
                    impact = EditorGUILayout.IntSlider("Estimated impact (0 unknown)", impact, 0, 3);
                    if (GUILayout.Button("Save review decision")) Run(() => OptimizationReviewStore.SetDecision(selectedKey, suppressed, reason, cost, impact));
                }
            }
            catch (Exception ex) { EditorGUILayout.HelpBox(ex.Message, MessageType.Warning); }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Controlled texture change", EditorStyles.boldLabel);
            texture = (Texture2D)EditorGUILayout.ObjectField("Default 2D texture", texture, typeof(Texture2D), false);
            platform = EditorGUILayout.Popup("Platform", platform == "Android" ? 0 : 1, new[] { "Android", "iPhone" }) == 0 ? "Android" : "iPhone";
            maxSize = EditorGUILayout.IntField("New platform Max Size", maxSize);
            if (GUILayout.Button("Create preview (no asset write)")) Run(() => { preview = VerifiedTextureChange.Preview(AssetDatabase.GetAssetPath(texture), platform, maxSize); reviewed = false; status = JsonUtility.ToJson(preview, true); });
            if (preview != null)
            {
                EditorGUILayout.HelpBox(preview.assetPath + " / " + preview.platform + ": " + preview.beforeSize + " → " + preview.afterSize + "\n" + preview.note, MessageType.Warning);
                reviewed = EditorGUILayout.ToggleLeft("I reviewed this exact texture usage and accept possible quality loss", reviewed);
                using (new EditorGUI.DisabledScope(!reviewed || preview.status != "Preview"))
                    if (GUILayout.Button("Apply this preview and verify import")) Run(() => { preview = VerifiedTextureChange.Apply(preview.id, reviewed); status = JsonUtility.ToJson(preview, true); });
            }
            foreach (var change in VerifiedTextureChange.History())
            {
                EditorGUILayout.LabelField(change.assetPath + " — " + change.status, EditorStyles.wordWrappedLabel);
                if (change.status.StartsWith("Applied;") && GUILayout.Button("Rollback " + change.id)) Run(() => { status = JsonUtility.ToJson(VerifiedTextureChange.Rollback(change.id), true); });
            }
            EditorGUILayout.TextArea(status ?? "No decision selected", GUILayout.MinHeight(80));
            EditorGUILayout.EndScrollView();
        }
    }
}
