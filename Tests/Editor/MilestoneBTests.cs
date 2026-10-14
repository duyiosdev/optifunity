using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Module1;
using Optifunity.Editor.Module3;
using Optifunity.Editor.Module4;
using Optifunity.Editor.Module5;
using Optifunity.Editor.Module6;
using Optifunity.Runtime;

namespace Optifunity.Tests
{
    public class MilestoneBTests
    {
        [TestCase("void Update(){ Helper(); } void Helper(){ var a=new int[4]; }", true)]
        [TestCase("void Update(){ Helper(); } void Helper(){ Next(); } void Next(){ var a=new object(); }", true)]
        [TestCase("void Update(){ Helper(); } void Helper(){ var a=new UnityEngine.Vector4(); }", false)]
        [TestCase("void Update(){} void Helper(){ var a=new int[4]; }", false)]
        [TestCase("void Update(){ Helper(); } void Helper(){ System.Action a=()=>{var b=new int[4];}; }", false)]
        [TestCase("void Update(){ Helper(); } void Helper(){ Helper(); }", false)]
        public void B_CallGraphCorpus(string body, bool allocation)
        {
            SemanticCodeAnalyzer.Clear();
            var issues = CallGraphAnalyzer.Analyze("BGraph.cs", "class BGraph:UnityEngine.MonoBehaviour {" + body + "}");
            Assert.That(issues.Any(i => i.Title == "Allocation reachable through helper"), Is.EqualTo(allocation));
            Assert.That(issues.All(i => i.EvidenceLevel == "Advisory"), Is.True);
            SemanticCodeAnalyzer.Clear();
        }
        [Test] public void B_DifferentCallPathsKeepSeparateReviewKeys()
        {
            SemanticCodeAnalyzer.Clear();
            var issues = CallGraphAnalyzer.Analyze("BGraph.cs",
                "class BGraph:UnityEngine.MonoBehaviour { void Update(){ One(); } void LateUpdate(){ Two(); } void One(){ Shared(); } void Two(){ Shared(); } void Shared(){ var a=new int[1]; } }")
                .Where(i => i.RuleId == "code.callgraph").ToArray();
            Assert.That(issues.Length, Is.EqualTo(2));
            Assert.That(issues.Select(Optifunity.Editor.Core.OptimizationReviewStore.Key).Distinct().Count(), Is.EqualTo(2));
        }
        [TestCase("", "False")]
        [TestCase("void OnDestroy(){ E -= Handler; }", "True")]
        public void B_Ownership(string cleanup, string expected)
        {
            SemanticCodeAnalyzer.Clear();
            var issues = CallGraphAnalyzer.Analyze("Owner.cs", "class Owner { static event System.Action E; void Start(){ E += Handler; } void Handler(){} " + cleanup + "}");
            Assert.That(issues.Single().Description, Does.Contain("same type=" + expected));
            Assert.That(issues.Single().Description, Does.Contain("not a leak verdict"));
            SemanticCodeAnalyzer.Clear();
        }
        [Test] public void B_AddressablesConfiguredRootOrExplicitAbsence()
        {
            var type = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor");
            if (type == null)
            {
                Assert.That(ProjectInventoryScanner.Scan(false, selectedPaths: Array.Empty<string>()).addressablesStatus, Does.StartWith("Not applicable"));
                return;
            }
            string folder = "Assets/OptifunityAddressables-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            try
            {
                var settings = type.GetMethod("Create").Invoke(null, new object[] { folder, "Settings", true, true });
                string path = folder + "/material.mat";
                AssetDatabase.CreateAsset(new Material(Shader.Find("Hidden/InternalErrorShader")), path);
                var group = type.GetProperty("DefaultGroup").GetValue(settings);
                type.GetMethod("CreateOrMoveEntry").Invoke(settings, new object[] { AssetDatabase.AssetPathToGUID(path), group, false, false });
                AssetDatabase.SaveAssets();
                byte[] before = File.ReadAllBytes(folder + "/Settings.asset");
                var report = ProjectInventoryScanner.Scan(false, selectedPaths: new[] { path });
                Assert.That(report.addressablesStatus, Does.StartWith("Configured roots"));
                Assert.That(report.addressableRoots, Does.Contain(path));
                Assert.That(File.ReadAllBytes(folder + "/Settings.asset"), Is.EqualTo(before));
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }
        [Test] public void B_InventorySubassetsDependenciesAndCancel()
        {
            string folder = "Assets/OptifunityB-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            try
            {
                var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
                string path = folder + "/material.mat";
                AssetDatabase.CreateAsset(material, path);
                AssetDatabase.AddObjectToAsset(new Mesh { name = "SubMesh" }, material);
                AssetDatabase.SaveAssets();
                var report = ProjectInventoryScanner.Scan(false, selectedPaths: new[] { path });
                Assert.That(report.assets.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(report.assets.Select(a => a.localId).Distinct().Count(), Is.EqualTo(report.assets.Count));
                Assert.That(report.assets.All(a => a.path == path), Is.True);
                Assert.That(ProjectInventoryScanner.Scan(false, () => true, new[] { path }).completion, Does.StartWith("Incomplete"));
                ProjectInventoryScanner.ReadBuildReport(report, null);
                Assert.That(report.buildStatus, Does.StartWith("Unknown"));
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }
        [Test] public void B_RenderingPrefabUsageDoesNotModifyAsset()
        {
            string path = "Assets/OptifunityB-" + Guid.NewGuid().ToString("N") + ".prefab";
            var root = new GameObject("Quoted \"root\"");
            try
            {
                new GameObject("Camera", typeof(Camera)).transform.SetParent(root.transform);
                new GameObject("Canvas", typeof(Canvas), typeof(CanvasRenderer)).transform.SetParent(root.transform);
                new GameObject("Particles", typeof(ParticleSystem)).transform.SetParent(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                byte[] before = File.ReadAllBytes(path);
                var report = RenderingUsageScanner.Scan(new[] { path });
                Assert.That(report.errors, Is.Empty);
                Assert.That(report.usages.Any(x => x.kind == "Camera"), Is.True);
                Assert.That(report.usages.Any(x => x.kind == "Canvas"), Is.True);
                Assert.That(report.usages.Any(x => x.kind == "ParticleSystem"), Is.True);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); AssetDatabase.DeleteAsset(path); }
        }
        [Test] public void B_MissingProfilerFrameIsUnknown() => Assert.That(MultiThreadFrameReader.Read(-1).status, Does.StartWith("Unknown"));
        [Test] public void B_MemoryDiffIsSignedAndChecksContext()
        {
            var before = new MemoryEvidence { projectPath = "fixture", unityVersion = "6", types = new List<MemoryTypeTotal> { new MemoryTypeTotal { type = "Mesh", count = 2, bytes = 100 } } };
            var after = new MemoryEvidence { projectPath = "fixture", unityVersion = "6", types = new List<MemoryTypeTotal> { new MemoryTypeTotal { type = "Mesh", count = 1, bytes = 20 } } };
            Assert.That(MemoryEvidenceReader.Compare(before, after).types.Single().byteDelta, Is.EqualTo(-80));
            after.projectPath = "another";
            Assert.Throws<ArgumentException>(() => MemoryEvidenceReader.Compare(before, after));
        }
        private static DeviceCapture Capture() => new DeviceCapture { scenario = "fixture", device = "simulated", operatingSystem = "fixture", unityVersion = "6000.0.58f2", graphicsApi = "fixture", quality = "fixture", project = "test", targetFps = 30,
            samples = new List<DeviceSample> { new DeviceSample { elapsedSeconds = 1, frameMs = 10, unityAllocatedBytes = 100 },
                new DeviceSample { elapsedSeconds = 2, frameMs = 20, unityAllocatedBytes = 200 }, new DeviceSample { elapsedSeconds = 3, frameMs = 100, unityAllocatedBytes = 150 } } };
        [Test] public void B_PercentilesHitchAndMissingMetrics()
        {
            var summary = DeviceCaptureAnalysis.Summarize(Capture());
            Assert.That(summary.p50Ms, Is.EqualTo(20)); Assert.That(summary.p95Ms, Is.EqualTo(100));
            Assert.That(summary.hitches, Is.EqualTo(1)); Assert.That(summary.peakUnityAllocatedBytes, Is.EqualTo(200));
            Assert.That(summary.peakResidentBytes, Is.EqualTo(-1)); Assert.That(summary.peakThermalCelsius, Is.EqualTo(-1));
            Assert.That(DeviceCaptureAnalysis.Compare(Capture(), Capture()).limitations.Any(x => x.Contains("Thermal")), Is.True);
        }
        [Test] public void B_ComparisonRejectsDifferentContextOrInvalidData()
        {
            var after = Capture(); after.scenario = "different";
            Assert.Throws<ArgumentException>(() => DeviceCaptureAnalysis.Compare(Capture(), after));
            after = Capture(); after.project = "";
            Assert.Throws<ArgumentException>(() => DeviceCaptureAnalysis.Compare(Capture(), after));
            after = Capture(); after.samples[1].frameMs = float.NaN;
            Assert.Throws<ArgumentException>(() => DeviceCaptureAnalysis.Summarize(after));
            after = Capture(); after.samples[1].elapsedSeconds = 0;
            Assert.Throws<ArgumentException>(() => DeviceCaptureAnalysis.Summarize(after));
            after = Capture(); after.samples[0].residentBytes = 1000;
            Assert.Throws<ArgumentException>(() => DeviceCaptureAnalysis.Summarize(after));
        }
        [Test] public void B_ComparisonExportPreservesBothCaptureContexts()
        {
            var before = Capture(); before.sessionId = "before"; before.variant = "baseline"; before.buildId = "build-1"; before.capturedAt = "2026-10-14T00:00:00Z";
            var after = Capture(); after.sessionId = "after"; after.variant = "candidate"; after.buildId = "build-2"; after.capturedAt = "2026-10-14T01:00:00Z";
            var exported = JsonUtility.ToJson(DeviceCaptureAnalysis.Compare(before, after));
            Assert.That(exported, Does.Contain("\"beforeContext\""));
            Assert.That(exported, Does.Contain("\"afterContext\""));
            Assert.That(exported, Does.Contain("build-1"));
            Assert.That(exported, Does.Contain("build-2"));
            Assert.That(exported, Does.Contain("\"project\":\"test\""));
            Assert.That(exported, Does.Contain("\"device\":\"simulated\""));
        }
        [Test] public void B_BudgetRequiresResidentEvidence()
        {
            var capture = Capture(); capture.memoryBudgetBytes = 50;
            Assert.That(DeviceCaptureAnalysis.Summarize(capture).memoryBudgetStatus, Does.StartWith("Unknown"));
            capture.samples[0].residentBytes = 100; capture.samples[0].externalMetricSource = "fixture RSS";
            Assert.That(DeviceCaptureAnalysis.Summarize(capture).memoryBudgetStatus, Does.StartWith("Observed"));
        }
        [Test] public void B_EvidenceWindowAndOptionalMemoryProfilerMenu()
        {
            Assert.That(EditorApplication.ExecuteMenuItem("Tools/Optifunity/Evidence and Benchmarks"), Is.True);
            EditorWindow.GetWindow<Optifunity.Editor.UI.EvidenceWindow>().Close();
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == "com.unity.memoryprofiler");
            if (package != null)
            {
                Assert.That(EditorApplication.ExecuteMenuItem("Window/Analysis/Memory Profiler"), Is.True);
                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType().Assembly.GetName().Name == "Unity.MemoryProfiler.Editor")) window.Close();
            }
        }
        [Test] public void B_LoadingComparisonAndMemoryCaptureValidation()
        {
            var before = Capture(); var after = Capture();
            before.loading.Add(new LoadingMeasurement { name = "Level", milliseconds = 100 });
            after.loading.Add(new LoadingMeasurement { name = "Level", milliseconds = 80 });
            Assert.That(DeviceCaptureAnalysis.Compare(before, after).loading.Single().deltaMs, Is.EqualTo(-20));
            Assert.Throws<ArgumentException>(() => MemoryEvidenceReader.CaptureNative("not-a-snapshot.json", null));
            after.loading.Add(new LoadingMeasurement { name = "Level", milliseconds = 50 });
            Assert.Throws<ArgumentException>(() => DeviceCaptureAnalysis.Compare(before, after));
        }
        [Test] public void B_RecorderRequiresExplicitScenario()
        {
            var go = new GameObject("recorder-test");
            try { var recorder = go.AddComponent<DeviceEvidenceRecorder>(); Assert.That(recorder.IsRecording, Is.False); Assert.Throws<InvalidOperationException>(() => recorder.Begin()); }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
