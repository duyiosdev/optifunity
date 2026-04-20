using System.Collections.Generic;
using NUnit.Framework;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module1;

namespace Optifunity.Tests
{
    /// <summary>
    /// Unit Tests cho các module Optifunity plugin.
    /// Chạy qua Window > General > Test Runner > EditMode
    /// </summary>
    public class AuditTests
    {
        // ─── Tests: PlatformConfig Budget Calculation ─────────────────────────

        [Test]
        public void Budget_2GB_ShouldReturn70Percent()
        {
            PlatformConfig.AndroidPhysicalRamMB = 2048;
            float expected = 2048f * 0.70f;
            Assert.AreEqual(expected, PlatformConfig.AvailableBudgetMB, 0.1f);
        }

        [Test]
        public void Budget_2GB_TextureShouldBe80MB()
        {
            PlatformConfig.AndroidPhysicalRamMB = 2048;
            Assert.AreEqual(80f, PlatformConfig.TextureBudgetMB, 0.1f);
        }

        [Test]
        public void Budget_4GB_TextureShouldBe150MB()
        {
            PlatformConfig.AndroidPhysicalRamMB = 4096;
            Assert.AreEqual(150f, PlatformConfig.TextureBudgetMB, 0.1f);
        }

        [Test]
        public void Budget_6GB_TextureShouldBe250MB()
        {
            PlatformConfig.AndroidPhysicalRamMB = 6144;
            Assert.AreEqual(250f, PlatformConfig.TextureBudgetMB, 0.1f);
        }

        [Test]
        public void AndroidTier_2GB_ShouldBeLow()
        {
            PlatformConfig.AndroidPhysicalRamMB = 2048;
            Assert.AreEqual(Runtime.AndroidDeviceTier.Low, PlatformConfig.GetAndroidTier());
        }

        [Test]
        public void AndroidTier_4GB_ShouldBeMid()
        {
            PlatformConfig.AndroidPhysicalRamMB = 4096;
            Assert.AreEqual(Runtime.AndroidDeviceTier.Mid, PlatformConfig.GetAndroidTier());
        }

        [Test]
        public void AndroidTier_8GB_ShouldBeHigh()
        {
            PlatformConfig.AndroidPhysicalRamMB = 8192;
            Assert.AreEqual(Runtime.AndroidDeviceTier.High, PlatformConfig.GetAndroidTier());
        }

        [Test]
        public void ASTC_2GB_ShouldRecommend8x8()
        {
            PlatformConfig.AndroidPhysicalRamMB = 2048;
            string block = PlatformConfig.GetRecommendedASTCBlockSize();
            Assert.IsTrue(block.Contains("8x8"), $"Expected 8x8 in '{block}'");
        }

        [Test]
        public void MaxTexSize_2GB_ShouldBe512()
        {
            PlatformConfig.AndroidPhysicalRamMB = 2048;
            Assert.AreEqual(512, PlatformConfig.GetRecommendedMaxTextureSize());
        }

        // ─── Tests: GCAllocAnalyzer ────────────────────────────────────────────

        [Test]
        public void GCAlloc_DetectsNewAllocationInUpdate()
        {
            string[] lines =
            {
                "void Update() {",
                "    var items = new List<int>();",
                "}"
            };
            var issues = GCAllocAnalyzer.Analyze("TestScript.cs", lines);
            Assert.IsTrue(issues.Count > 0, "Should detect 'new' allocation in Update");
            Assert.IsTrue(issues.Exists(i => i.Title.Contains("mới") || i.Title.Contains("new")));
        }

        [Test]
        public void GCAlloc_DetectsStringConcatInUpdate()
        {
            string[] lines =
            {
                "void Update() {",
                "    string s = \"Hello\" + playerName;",
                "}"
            };
            var issues = GCAllocAnalyzer.Analyze("TestScript.cs", lines);
            Assert.IsTrue(issues.Exists(i => i.Title.Contains("String") || i.Title.Contains("chuỗi")));
        }

        [Test]
        public void GCAlloc_NoIssueOutsideLoopMethod()
        {
            string[] lines =
            {
                "void Start() {",
                "    var items = new List<int>();",
                "}"
            };
            var issues = GCAllocAnalyzer.Analyze("TestScript.cs", lines);
            // new inside Start() should NOT be flagged
            Assert.AreEqual(0, issues.Count, "Should not flag new() outside Update/FixedUpdate/LateUpdate");
        }

        [Test]
        public void GCAlloc_DetectsReturnNewArrayInUpdate()
        {
            string[] lines =
            {
                "void Update() {",
                "    return new int[10];",
                "}"
            };
            var issues = GCAllocAnalyzer.Analyze("TestScript.cs", lines);
            Assert.IsTrue(issues.Exists(i => i.Title.Contains("mảng") || i.Title.Contains("array")));
        }

        // ─── Tests: BoxingAnalyzer ────────────────────────────────────────────

        [Test]
        public void Boxing_DetectsExplicitObjectCast()
        {
            string[] lines =
            {
                "void SomeMethod() {",
                "    object boxed = (object)myVector;",
                "}"
            };
            var issues = BoxingAnalyzer.Analyze("TestScript.cs", lines);
            Assert.IsTrue(issues.Exists(i => i.Title.Contains("Boxing") || i.Title.Contains("object")));
        }

        [Test]
        public void Boxing_DetectsNonGenericCollection()
        {
            string[] lines =
            {
                "private ArrayList myList = new ArrayList();",
            };
            var issues = BoxingAnalyzer.Analyze("TestScript.cs", lines);
            Assert.IsTrue(issues.Exists(i => i.Title.Contains("collection") || i.Title.Contains("boxing")),
                "Should detect ArrayList as non-generic boxing risk");
        }

        // ─── Tests: MemLeakAnalyzer ────────────────────────────────────────────

        [Test]
        public void MemLeak_DetectsEventSubscribeWithoutUnsubscribe()
        {
            string[] lines =
            {
                "void OnEnable() {",
                "    GameManager.OnGameOver += HandleGameOver;",
                "}",
                "// No OnDestroy",
            };
            var issues = MemLeakAnalyzer.Analyze("TestScript.cs", lines);
            Assert.IsTrue(issues.Exists(i => i.Severity == IssueSeverity.Error &&
                                             (i.Title.Contains("sự kiện") || i.Title.Contains("event"))));
        }

        [Test]
        public void MemLeak_NoIssueWhenBothSubscribeAndUnsubscribeWithOnDestroy()
        {
            string[] lines =
            {
                "void OnEnable() {",
                "    GameManager.OnGameOver += HandleGameOver;",
                "}",
                "void OnDestroy() {",
                "    GameManager.OnGameOver -= HandleGameOver;",
                "}"
            };
            var issues = MemLeakAnalyzer.Analyze("TestScript.cs", lines);
            // Should have 0 Error-severity issues about events
            Assert.IsFalse(issues.Exists(i =>
                i.Severity == IssueSeverity.Error &&
                (i.Title.Contains("đăng ký") || i.Title.Contains("subscribe"))));
        }

        // ─── Tests: ReportEngine ──────────────────────────────────────────────

        [Test]
        public void ReportEngine_HealthScore_100_WhenNoIssues()
        {
            var report = ReportEngine.BeginScan();
            ReportEngine.AddIssues(IssueModule.CodeAnalysis, new List<PerformanceIssue>());
            Assert.AreEqual(100, report.HealthScore);
        }

        [Test]
        public void ReportEngine_HealthScore_DecreaseOnErrors()
        {
            var report = ReportEngine.BeginScan();
            var issues = new List<PerformanceIssue>
            {
                new PerformanceIssue { Severity = IssueSeverity.Error,   Title = "E1" },
                new PerformanceIssue { Severity = IssueSeverity.Error,   Title = "E2" },
                new PerformanceIssue { Severity = IssueSeverity.Warning, Title = "W1" },
            };
            ReportEngine.AddIssues(IssueModule.URPDiagnostics, issues);
            Assert.IsTrue(report.HealthScore < 80, $"HealthScore should decrease. Got: {report.HealthScore}");
        }

        [Test]
        public void ReportEngine_ExportMarkdown_ContainsProjectName()
        {
            var report = ReportEngine.BeginScan();
            string md = ReportEngine.ExportMarkdown(report);
            Assert.IsTrue(md.Contains("Optifunity Performance Report"));
        }
    }
}
