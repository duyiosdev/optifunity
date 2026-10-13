using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module5;
using Optifunity.Editor.UI;

namespace Optifunity.Tests
{
    public class AcceptanceTests
    {
        [Test] public void CatalogRejectsMissingEvidenceAndUnsupportedFix()
        {
            var rule = JsonUtility.FromJson<KnowledgeRule>(JsonUtility.ToJson(RuleCatalog.Get("code.boxing")));
            rule.sources = new string[0];
            Assert.Throws<InvalidDataException>(() => RuleCatalog.Validate(rule));
            rule = JsonUtility.FromJson<KnowledgeRule>(JsonUtility.ToJson(RuleCatalog.Get("code.boxing")));
            rule.fixPolicy = "Automatic";
            Assert.Throws<InvalidDataException>(() => RuleCatalog.Validate(rule));
            rule.fixPolicy = "Manual only"; rule.tradeoffs = "";
            Assert.Throws<InvalidDataException>(() => RuleCatalog.Validate(rule));
        }
        [Test] public void UnknownRuleCannotRetainConfirmedSeverity()
        {
            ReportEngine.BeginScan();
            var issue = new PerformanceIssue { RuleId = "missing.rule", Severity = IssueSeverity.Error };
            ReportEngine.AddIssues(IssueModule.CodeAnalysis, new[] { issue });
            Assert.That(issue.RuleId, Is.EqualTo("legacy.unverified"));
            Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Info));
            Assert.That(issue.Description, Does.StartWith("UNVERIFIED"));
        }
        [Test] public void AllocationMarkerIsNotCollectionInCategoryChart()
        {
            var classify = typeof(FrameDataReader).GetMethod("DetermineCategory", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(classify.Invoke(null, new object[] { "GC.Alloc" }), Is.Not.EqualTo(ProfilerCategory.GarbageCollection));
            Assert.That(classify.Invoke(null, new object[] { "GC.Collect" }), Is.EqualTo(ProfilerCategory.GarbageCollection));
        }
        [Test] public void CaptureResetClearsSelectionAndNotifiesWindows()
        {
            bool notified = false;
            System.Action listener = () => notified = true;
            SpikeDetector.OnCaptureReset += listener;
            try
            {
                typeof(SpikeDetector).GetField("_lastSelectedFrame", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 42);
                SpikeDetector.ResetCapture();
                Assert.That(notified, Is.True);
                Assert.That(SpikeDetector.LastSelectedFrame, Is.EqualTo(-1));
                Assert.That(SpikeDetector.FrameBuffer, Is.Empty);
            }
            finally { SpikeDetector.OnCaptureReset -= listener; }
        }
        [Test] public void DashboardFullThenModuleScanKeepsCoverageInExport()
        {
            if (!File.Exists(".optifunity-fixture")) Assert.Ignore("Only scan the owned disposable fixture.");
            DashboardWindow window = null;
            try
            {
                Assert.That(EditorApplication.ExecuteMenuItem("Tools/Optifunity/Dashboard"), Is.True);
                window = EditorWindow.GetWindow<DashboardWindow>();
                typeof(DashboardWindow).GetMethod("RunFullScan", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
                Assert.That(ReportEngine.LastReport.ModuleCoverage.Count, Is.EqualTo(5));
                var assetIssues = ReportEngine.LastReport.AssetIssues;
                typeof(DashboardWindow).GetMethod("RunCodeScan", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
                Assert.That(ReportEngine.LastReport.AssetIssues, Is.SameAs(assetIssues));
                string markdown = ReportEngine.ExportMarkdown();
                foreach (var coverage in ReportEngine.LastReport.ModuleCoverage) Assert.That(markdown, Does.Contain(coverage));
                File.WriteAllText("fixture-report.md", markdown);
            }
            finally { if (window != null) window.Close(); }
        }
    }
}
