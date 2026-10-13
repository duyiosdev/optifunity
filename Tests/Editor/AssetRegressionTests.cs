using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module2;
using Optifunity.Editor.Module4;
using Optifunity.Editor.Module5;

namespace Optifunity.Tests
{
    public class AssetRegressionTests
    {
        [Test] public void T10_AudioOverrideUsesSelectedPlatform()
        {
            string path = "Assets/OptifunityAudio-" + Guid.NewGuid().ToString("N") + ".wav";
            var originalPlatform = PlatformConfig.ActivePlatform;
            try
            {
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 128);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)1); writer.Write(22050); writer.Write(44100);
                    writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                    writer.Write(128); writer.Write(new byte[128]);
                }
                AssetDatabase.ImportAsset(path);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                var defaults = importer.defaultSampleSettings; defaults.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = defaults;
                var settings = defaults; settings.loadType = AudioClipLoadType.Streaming;
                Assert.That(importer.SetOverrideSampleSettings("Android", settings)); importer.SaveAndReimport();
                var read = typeof(AudioAuditor).GetMethod("EffectiveSampleSettings", BindingFlags.NonPublic | BindingFlags.Static);
                PlatformConfig.ActivePlatform = Optifunity.Runtime.TargetPlatformType.Android;
                Assert.That(((AudioImporterSampleSettings)read.Invoke(null, new object[] { importer })).loadType, Is.EqualTo(AudioClipLoadType.Streaming));
                PlatformConfig.ActivePlatform = Optifunity.Runtime.TargetPlatformType.iOS;
                Assert.That(((AudioImporterSampleSettings)read.Invoke(null, new object[] { importer })).loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
            }
            finally { PlatformConfig.ActivePlatform = originalPlatform; AssetDatabase.DeleteAsset(path); }
        }
        [Test] public void T22_SimpleRendererDoesNotRequireLODAndMpbFilterIsInformation()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "OptifunityFilter-" + Guid.NewGuid().ToString("N");
            try
            {
                var findings = SceneMeshRendererScanner.AnalyzeOpenScenes();
                Assert.That(findings.Where(x => (x.CodeLocation ?? "").Contains(cube.name)).Any(x => x.Title.Contains("LODGroup")), Is.False);
                var filtered = SceneMeshRendererScanner.AnalyzeOpenScenes(new SceneMeshRendererScanner.ScanOptions {
                    EnableMaterialPropertyBlockFilter = true, MaterialPropertyBlockExpectedOn = false });
                Assert.That(filtered.Any(x => (x.CodeLocation ?? "").Contains(cube.name) && x.Title.Contains("MaterialPropertyBlock")));
                Assert.That(filtered.Where(x => (x.CodeLocation ?? "").Contains(cube.name) && x.Title.Contains("MaterialPropertyBlock"))
                    .All(x => x.Severity == IssueSeverity.Info));
                var block = new MaterialPropertyBlock(); block.SetFloat("_Test", 1);
                cube.GetComponent<Renderer>().SetPropertyBlock(block, 0);
                var method = typeof(SceneMeshRendererScanner).GetMethod("HasMaterialPropertyBlock", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method.Invoke(null, new object[] { cube.GetComponent<Renderer>() }), Is.EqualTo(true));
            }
            finally { UnityEngine.Object.DestroyImmediate(cube); }
        }
        [Test] public void T15_CollectionTimeIsMeasuredNotInvented()
        {
            var frame = new FrameSnapshot { IsValid = true, TotalCpuTimeMs = 40 };
            var sample = new ProfilerSample { Name = "GC.Collect", TotalTimeMs = 0.05f };
            frame.TopLevelSamples.Add(sample); frame.SamplesByName["GC.Collect"] = new System.Collections.Generic.List<ProfilerSample> { sample };
            var report = BottleneckClassifier.Classify(frame, 16);
            Assert.That(report.PrimaryDescription, Does.Contain("0.05"));
        }
        [Test] public void T14_MeasuredGPUCanSupportHypothesis()
        {
            var report = BottleneckClassifier.Classify(new FrameSnapshot { IsValid = true, TotalCpuTimeMs = 40, TotalGpuTimeMs = 35 }, 16);
            Assert.That(report.PrimaryBottleneck, Is.EqualTo(BottleneckType.GPUBound));
            Assert.That(report.PrimaryDescription, Does.Contain("Hypothesis"));
        }
    }
}
