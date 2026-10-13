using System;
using System.Linq;
using NUnit.Framework;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module1;
using Optifunity.Editor.Module4;
using Optifunity.Editor.Module5;
using UnityEditor;

namespace Optifunity.Tests
{
    public class AuditTests
    {
        private int ram;
        [SetUp] public void SetUp() { ram = PlatformConfig.AndroidPhysicalRamMB; SemanticCodeAnalyzer.Clear(); }
        [TearDown] public void TearDown() { PlatformConfig.AndroidPhysicalRamMB = ram; SemanticCodeAnalyzer.Clear(); }
        private static string[] Source(string body) => new[] { "using System; using System.Collections; using System.Collections.Generic; using UnityEngine; class Fixture : MonoBehaviour { " + body + " }" };

        [TestCase("void Update() { var x = new int[4]; }")]
        [TestCase("void Update()\n{\nvar x = new int[4];\n}")]
        public void T01_T02_ArrayAllocation(string method) =>
            Assert.That(GCAllocAnalyzer.Analyze("Fixture.cs", Source(method)).Count(i => i.RuleId == "code.allocation"), Is.EqualTo(1));
        [Test] public void T02_Vector4DoesNotAllocate() =>
            Assert.That(GCAllocAnalyzer.Analyze("Fixture.cs", Source("void Update() { var x = new Vector4(); }")), Is.Empty);
        [TestCase("string value = \"s\"; object x = (object)value;", 0)]
        [TestCase("int value = 2; object x = (object)value;", 1)]
        [TestCase("int value = 2; object x = value;", 1)]
        public void T03_BoxingUsesResolvedConversion(string body, int expected) =>
            Assert.That(BoxingAnalyzer.Analyze("Fixture.cs", Source("void Test() { " + body + " }" )).Count(i => i.RuleId == "code.boxing"), Is.EqualTo(expected));
        [Test] public void T04_GenericDeclarationIsNotBoxing() =>
            Assert.That(BoxingAnalyzer.Analyze("Fixture.cs", Source("IDictionary<string,int> values;")), Is.Empty);
        [Test] public void T04_UnresolvedTypeIsAdvisory() =>
            Assert.That(GCAllocAnalyzer.Analyze("Fixture.cs", Source("void Update() { var x = new MissingType(); }" )).All(i => i.EvidenceLevel == "Advisory"));
        [Test] public void T05_ArithmeticIsNotEvent() =>
            Assert.That(MemLeakAnalyzer.Analyze("Fixture.cs", Source("int total; void Update() { total += 2; }")), Is.Empty);
        [Test] public void T05_EventIsAdvisoryNotLeakProof()
        {
            var issues = MemLeakAnalyzer.Analyze("Fixture.cs", Source("event Action Event; void Handler() {} void OnEnable() { Event += Handler; } void OnDisable() { Event -= Handler; }"));
            Assert.That(issues.Count, Is.EqualTo(1)); Assert.That(issues[0].EvidenceLevel, Is.EqualTo("Advisory"));
        }
        [Test] public void T06_LoopDoesNotSpill()
        {
            var issues = ResourceManagementAnalyzer.Analyze("Fixture.cs", Source("void A() { for(int i=0;i<1;i++) { UnityEngine.Object.Destroy(null); } } void B() { UnityEngine.Object.Destroy(null); }"));
            Assert.That(issues.Count, Is.EqualTo(1));
        }
        [Test] public void T07_CommentsStringsAndDisabledCodeDoNotAllocate()
        {
            var lines = new[] { "using UnityEngine; class Fixture : MonoBehaviour { void Update() {", "// new int[3]", "var s = \"new int[4]\";", "#if NEVER_DEFINED", "var a = new int[5];", "#endif", "} }" };
            Assert.That(GCAllocAnalyzer.Analyze("Fixture.cs", lines), Is.Empty);
        }
        [Test] public void T07_NestedLambdaIsNotAssumedToRunEveryFrame() =>
            Assert.That(GCAllocAnalyzer.Analyze("Fixture.cs", Source("void Update() { Action callback = () => { var x = new int[3]; }; }")), Is.Empty);
        [Test] public void AllocationOutsideGameplayIsNotFlagged() =>
            Assert.That(GCAllocAnalyzer.Analyze("Fixture.cs", Source("void Start() { var x = new int[3]; }")), Is.Empty);
        [Test] public void T08_UnavailableCaptureIsNotZeroMeasurement() =>
            Assert.That(DrawCallAnalyzer.CaptureStats().IsCaptured, Is.False);
        [Test] public void T13_SlowUnknownFrameIsNotBalanced()
        {
            var report = BottleneckClassifier.Classify(new FrameSnapshot { IsValid = true, TotalCpuTimeMs = 100 }, 16);
            Assert.That(report.PrimaryBottleneck, Is.EqualTo(BottleneckType.Unknown));
            Assert.That(report.PrimaryConfidence, Is.EqualTo(0));
        }
        [TestCase("Gfx.WaitForPresent")]
        [TestCase("Gfx.WaitForPresentOnGfxThread")]
        public void T14_WaitAloneDoesNotProveGPU(string marker)
        {
            var frame = new FrameSnapshot { IsValid = true, TotalCpuTimeMs = 40, TotalGpuTimeMs = 2 };
            frame.SamplesByName[marker] = new System.Collections.Generic.List<ProfilerSample> { new ProfilerSample { Name = marker, TotalTimeMs = 30 } };
            Assert.That(BottleneckClassifier.Classify(frame, 16).PrimaryBottleneck, Is.Not.EqualTo(BottleneckType.GPUBound));
        }
        [Test] public void T15_AllocationIsNotCollection()
        {
            var frame = new FrameSnapshot { IsValid = true, TotalCpuTimeMs = 40, TotalGCAllocBytes = 200000 };
            frame.SamplesByName["GC.Alloc"] = new System.Collections.Generic.List<ProfilerSample> { new ProfilerSample { Name = "GC.Alloc", GCAllocBytes = 200000 } };
            Assert.That(BottleneckClassifier.Classify(frame, 16).PrimaryBottleneck, Is.Not.EqualTo(BottleneckType.GarbageCollection));
        }
        [Test] public void T18_T21_UnverifiedWritesCannotBeInvokedThroughIssue()
        {
            bool changed = false;
            var issue = new PerformanceIssue { CanAutoFix = true, AutoFixAction = () => changed = true };
            Assert.That(issue.CanAutoFix, Is.False); Assert.That(issue.AutoFixAction, Is.Null); Assert.That(changed, Is.False);
            Assert.Throws<InvalidOperationException>(() => SRPBatcherChecker.TryFixShader("Assets/Test.shader"));
            PlatformConfig.AutoFixEnabled = true; Assert.That(PlatformConfig.AutoFixEnabled, Is.False);
        }
        [Test] public void T19_IdentityIncludesInstanceAndProject()
        {
            var identity = new UnityMcpBridge.Connection { token = "secret", projectPath = "project", instanceId = "one" };
            Assert.That(UnityMcpBridge.MatchesIdentity(identity, "secret", "project", "one"));
            Assert.That(UnityMcpBridge.MatchesIdentity(identity, "secret", "project", "two"), Is.False);
            Assert.That(UnityMcpBridge.MatchesIdentity(identity, "wrong", "project", "one"), Is.False);
            Assert.That(UnityMcpBridge.MatchesIdentity(identity, "secret", "other", "one"), Is.False);
        }
        [Test] public void T22_InformationDoesNotReduceScore()
        {
            var report = new ScanReport(); report.CodeIssues.Add(new PerformanceIssue { Severity = IssueSeverity.Info });
            Assert.That(report.HealthScore, Is.EqualTo(100));
        }
        [Test] public void BudgetIsPluginPreset()
        {
            PlatformConfig.AndroidPhysicalRamMB = 2048;
            Assert.That(PlatformConfig.AvailableBudgetMB, Is.EqualTo(1433.6f).Within(0.1f));
        }
        [Test] public void ExportIncludesScopeAndEvidence()
        {
            var report = ReportEngine.BeginScan();
            ReportEngine.AddIssues(IssueModule.CodeAnalysis, new[] { new PerformanceIssue { RuleId = "code.boxing", Title = "Boxing", EvidenceLevel = "Confirmed static operation" } });
            string markdown = ReportEngine.ExportMarkdown(report);
            Assert.That(markdown, Does.Contain("**Scope:**")); Assert.That(markdown, Does.Contain("code.boxing"));
        }
    }
}
