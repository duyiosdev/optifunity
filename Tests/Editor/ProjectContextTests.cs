using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Optifunity.Editor.Module1;
using Optifunity.Editor.Core;

namespace Optifunity.Tests
{
    public class ProjectContextTests
    {
        [Test] public void ActualAsmdefReferencesAndDefinesResolve()
        {
            string path = "Assets/OptifunityValidation/Gameplay/ValidationGameplay.cs";
            if (!File.Exists(path)) Assert.Ignore("Run Tools~/Validate.ps1 to install the owned fixture.");
            SemanticCodeAnalyzer.Clear();
            var lines = File.ReadAllLines(path);
            var issues = GCAllocAnalyzer.Analyze(path, lines);
            Assert.That(issues.Where(x => x.RuleId == "code.unresolved"), Is.Empty, string.Join("\n", issues.Select(x => x.Description)));
            Assert.That(issues.Count(x => x.RuleId == "code.allocation"), Is.EqualTo(3));
            Assert.That(BoxingAnalyzer.Analyze(path, lines).Count(x => x.RuleId == "code.boxing"), Is.EqualTo(1));
            SemanticCodeAnalyzer.Clear();
        }
        [Test] public void SameAssemblySourcesReuseSemanticCompilation()
        {
            string first = "Assets/OptifunityValidation/Gameplay/ValidationGameplay.cs";
            string second = "Assets/OptifunityValidation/Gameplay/SecondGameplay.cs";
            if (!File.Exists(first) || !File.Exists(second)) Assert.Ignore("Run Tools~/Validate.ps1 to install the owned fixture.");
            SemanticCodeAnalyzer.Clear();
            var model = typeof(SemanticCodeAnalyzer).GetMethod("Model", BindingFlags.NonPublic | BindingFlags.Static);
            var cache = (System.Collections.IDictionary)typeof(SemanticCodeAnalyzer)
                .GetField("Compilations", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            model.Invoke(null, new object[] { first, string.Join("\n", File.ReadAllLines(first)) });
            var initial = cache.Values.Cast<object>().Single();
            model.Invoke(null, new object[] { second, string.Join("\n", File.ReadAllLines(second)) });
            Assert.That(cache.Values.Cast<object>().Single(), Is.SameAs(initial));
            SemanticCodeAnalyzer.Clear();
        }
        [Test] public void CodeScanFindingsUseLoadableProjectPaths()
        {
            if (!File.Exists(".optifunity-fixture")) Assert.Ignore("Only scan the owned disposable fixture.");
            var issue = CodeAnalysisRunner.RunAll(false).First(i => i.RuleId == "code.allocation" &&
                i.AssetPath == "Assets/OptifunityValidation/Gameplay/ValidationGameplay.cs");
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(issue.AssetPath), Is.Not.Null);
            Assert.That(issue.CodeLocation, Does.StartWith(issue.AssetPath + ":L"));
        }
        [Test] public void FixtureImportsHaveResolvableScripts()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.unity.render-pipelines.core/Runtime/Volume/VolumeProfile.cs");
            Assert.That(script, Is.Not.Null, "Core package script asset was not imported.");
            Assert.That(script.GetClass(), Is.EqualTo(typeof(VolumeProfile)));
            var global = AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.unity.render-pipelines.universal/Runtime/UniversalRenderPipelineGlobalSettings.cs");
            Assert.That(global, Is.Not.Null); Assert.That(global.GetClass()?.FullName, Is.EqualTo("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings"));
        }
        [Test] public void FixtureScanMetrics()
        {
            if (!File.Exists(".optifunity-fixture")) Assert.Ignore("Metrics only run in disposable fixture.");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            long before = GC.GetTotalMemory(false);
            var issues = CodeAnalysisRunner.RunAll(false);
            timer.Stop();
            File.WriteAllText("scan-metrics.json", JsonUtility.ToJson(new Metrics {
                elapsedMs = timer.Elapsed.TotalMilliseconds, managedBefore = before,
                managedAfter = GC.GetTotalMemory(false), issues = issues.Count,
                coverage = CodeAnalysisRunner.LastCoverage, unity = Application.unityVersion
            }, true));
            Assert.That(CodeAnalysisRunner.LastCoverage, Does.Not.StartWith("Incomplete"));
        }
        [Serializable] private class Metrics { public double elapsedMs; public long managedBefore, managedAfter; public int issues; public string coverage, unity; }
    }
}
