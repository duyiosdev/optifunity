using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module1;
using Optifunity.Editor.Module2;
using Optifunity.Editor.Module4;
using Optifunity.Editor.Module5;
using Optifunity.Editor.Module7;

namespace Optifunity.Tests
{
    public class IntegrationTests
    {
        [Test] public void CatalogHasSourcesForConfirmedRules()
        {
            foreach (string id in new[] { "code.allocation", "code.boxing", "code.resources", "code.event-lifetime", "render.shader-compatibility" })
            {
                var rule = RuleCatalog.Get(id);
                Assert.That(rule.ruleId, Is.EqualTo(id)); Assert.That(rule.sources, Is.Not.Empty);
                Assert.That(rule.verifiedAt, Is.Not.Empty); Assert.That(rule.fixPolicy, Is.EqualTo("Manual only"));
            }
        }
        [Test] public void T08_RealCounterAdapterAndMissingCounter()
        {
            var read = typeof(DrawCallAnalyzer).GetMethod("GetStat", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (string name in new[] { "setPassCalls", "drawCalls", "batches", "triangles", "vertices" })
                Assert.That((float)read.Invoke(null, new object[] { name }), Is.GreaterThanOrEqualTo(0));
            var error = Assert.Throws<TargetInvocationException>(() => read.Invoke(null, new object[] { "missingCounter" }));
            Assert.That(error.InnerException, Is.TypeOf<MissingMemberException>());
        }
        [Test] public void T09_QualityOverrideAndInheritance()
        {
            var originalDefault = GraphicsSettings.defaultRenderPipeline;
            var originalQuality = QualitySettings.renderPipeline;
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            var first = UniversalRenderPipelineAsset.Create(renderer);
            var second = UniversalRenderPipelineAsset.Create(renderer);
            first.shadowDistance = 20; second.shadowDistance = 60;
            try
            {
                GraphicsSettings.defaultRenderPipeline = first; QualitySettings.renderPipeline = null;
                var inherited = URPAssetScanner.Scan();
                Assert.That(inherited.ShadowDistance, Is.EqualTo(20)); Assert.That(inherited.GraphicsAndQualityPipelineMatch);
                QualitySettings.renderPipeline = second;
                var overridden = URPAssetScanner.Scan();
                Assert.That(overridden.ShadowDistance, Is.EqualTo(60));
                Assert.That(ProjectSettingsMismatchAnalyzer.Analyze(overridden).Any(i => i.Severity == IssueSeverity.Error), Is.False);
            }
            finally
            {
                QualitySettings.renderPipeline = originalQuality; GraphicsSettings.defaultRenderPipeline = originalDefault;
                UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); UnityEngine.Object.DestroyImmediate(renderer);
            }
        }
        [Test] public void T10_TextureOverrideWinsOverDefault()
        {
            string path = "Assets/OptifunityValidation-" + Guid.NewGuid().ToString("N") + ".png";
            var image = new Texture2D(4,4);
            var originalPlatform = PlatformConfig.ActivePlatform;
            try
            {
                File.WriteAllBytes(path, image.EncodeToPNG()); AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.maxTextureSize = 2048;
                var settings = importer.GetPlatformTextureSettings("Android");
                settings.overridden = true; settings.maxTextureSize = 128;
                importer.SetPlatformTextureSettings(settings); importer.SaveAndReimport();
                PlatformConfig.ActivePlatform = Optifunity.Runtime.TargetPlatformType.Android;
                var read = typeof(TextureAuditor).GetMethod("EffectiveMaxSize", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(read.Invoke(null, new object[] { importer }), Is.EqualTo(128));
                Assert.That(TextureAuditor.Audit().Any(i => i.AssetPath == path && i.Title.Contains("MaxSize")), Is.False);
                PlatformConfig.ActivePlatform = Optifunity.Runtime.TargetPlatformType.iOS;
                Assert.That(read.Invoke(null, new object[] { importer }), Is.EqualTo(2048));
            }
            finally { PlatformConfig.ActivePlatform = originalPlatform; AssetDatabase.DeleteAsset(path); UnityEngine.Object.DestroyImmediate(image); }
        }
        [Test] public void T11_T12_StrategiesAreNotContradictoryRequirements()
        {
            var report = MobileWorkflowGraphBuilder.BuildPipelineReport(new URPAssetScanner.URPSettingsSnapshot { IsURPActive = true });
            Assert.That(report.Pipelines.SelectMany(p => p.Checks).Where(c => c.Id == "grd_static_off" || c.Id == "sb_toggle")
                .All(c => c.Status == MobileNodeStatus.Advisory));
            Assert.That(report.Pipelines.SelectMany(p => p.Checks).First(c => c.Id == "grd_device").Status, Is.EqualTo(MobileNodeStatus.Unknown));
            Assert.That(report.Pipelines.SelectMany(p => p.Checks).First(c => c.Id == "grd_toggle").Status, Is.EqualTo(MobileNodeStatus.Unknown));
        }
        [Test] public void T16_ReusedFrameIndexDoesNotReturnOldCapture()
        {
            var field = typeof(SpikeAnalysisRunner).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static);
            var cache = (System.Collections.Generic.Dictionary<int, SpikeAnalysisReport>)field.GetValue(null);
            var stale = new SpikeAnalysisReport { FrameIndex = int.MaxValue, PrimaryTitle = "old capture" };
            cache[int.MaxValue] = stale;
            Assert.That(SpikeAnalysisRunner.AnalyzeFrame(int.MaxValue), Is.Not.SameAs(stale));
        }
        [Test] public void T17_ModuleScanPreservesOtherModuleResults()
        {
            ReportEngine.BeginScan();
            ReportEngine.AddIssues(IssueModule.AssetAudit, new[] { new PerformanceIssue { Title = "asset" } });
            ReportEngine.BeginModuleScan(IssueModule.CodeAnalysis);
            ReportEngine.AddIssues(IssueModule.CodeAnalysis, new[] { new PerformanceIssue { RuleId = "code.boxing", Title = "code" } });
            Assert.That(ReportEngine.LastReport.AssetIssues.Count, Is.EqualTo(1));
            ReportEngine.BeginModuleScan(IssueModule.CodeAnalysis);
            ReportEngine.AddIssues(IssueModule.CodeAnalysis, Array.Empty<PerformanceIssue>());
            Assert.That(ReportEngine.LastReport.CodeIssues, Is.Empty);
            Assert.That(ReportEngine.LastReport.AssetIssues.Count, Is.EqualTo(1));
        }
        [Test] public void T17_CancellationIsIncomplete()
        {
            string path = "Assets/OptifunityCancelFixture-" + Guid.NewGuid().ToString("N") + ".cs";
            try
            {
                File.WriteAllText(path, "class CancelFixture {}"); AssetDatabase.ImportAsset(path);
                CodeAnalysisRunner.RunAll(false, false, () => true);
                Assert.That(CodeAnalysisRunner.LastCoverage, Does.StartWith("Incomplete"));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
        [Test] public void T21_CommentOrMissingShaderIsNotCompatibilityProof()
        {
            var info = SRPBatcherChecker.CheckShader(null, "Assets/missing.shader");
            Assert.That(info.IncompatibilityReason, Does.StartWith("Unknown"));
            Assert.That(info.IsSRPBatcherCompatible, Is.Null);
        }
        private static async Task<(int status,string body)> Call(UnityMcpBridge.Connection connection, string path, string instance = null)
        {
            var request = (HttpWebRequest)WebRequest.Create(connection.url + path);
            request.Headers["X-Optifunity-Token"] = connection.token;
            request.Headers["X-Optifunity-Project"] = Uri.EscapeDataString(connection.projectPath);
            request.Headers["X-Optifunity-Instance"] = instance ?? connection.instanceId;
            request.Timeout = 6000;
            HttpWebResponse response;
            try { response = (HttpWebResponse)await request.GetResponseAsync(); }
            catch (WebException ex) when (ex.Response != null) { response = (HttpWebResponse)ex.Response; }
            using (response) using (var reader = new StreamReader(response.GetResponseStream()))
                return ((int)response.StatusCode, await reader.ReadToEndAsync());
        }
        [UnityTest] public IEnumerator T19_T20_LiveBridgeAuthAndJson()
        {
            var field = typeof(UnityMcpBridge).GetField("connection", BindingFlags.NonPublic | BindingFlags.Static);
            var connection = (UnityMcpBridge.Connection)field.GetValue(null);
            var wrong = Call(connection, "/status", "different-editor-same-path");
            while (!wrong.IsCompleted) yield return null;
            Assert.That(wrong.Result.status, Is.EqualTo(403));
            var oldCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
                var valid = Call(connection, "/status");
                while (!valid.IsCompleted) yield return null;
                Assert.That(valid.Result.status, Is.EqualTo(200));
                Assert.That(valid.Result.body, Does.Contain("\"status\":\"ok\""));
                var mutation = Call(connection, "/optimize");
                while (!mutation.IsCompleted) yield return null;
                Assert.That(mutation.Result.status, Is.EqualTo(403));
            }
            finally { CultureInfo.CurrentCulture = oldCulture; }
        }
    }
}
