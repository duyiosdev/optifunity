using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Optifunity.Editor.Core
{
    [Serializable] public class FindingDecision { public string key, reason, updatedAt; public bool suppressed; public int cost = 2, impact; }
    [Serializable] public class FindingState { public int version = 1; public string project; public List<string> baseline = new(); public List<string> baselineCoverage = new(); public string baselineAt; public List<FindingDecision> decisions = new(); }
    [Serializable] public class PrioritizedFinding
    {
        public string key, change, priorityReason, measuredImpact = "Unknown until linked to device measurements";
        public int evidenceRank, cost, impact;
        public bool suppressed;
        public string reason;
        public PerformanceIssue finding;
        public KnowledgeRule rule;
    }
    [Serializable] public class OptimizationReview
    {
        public string project, capturedAt, scope, completion, baselineAt;
        public List<string> baselineCoverage = new();
        public List<string> moduleCoverage = new();
        public List<PrioritizedFinding> findings = new();
        public List<string> absentBaselineKeys = new();
    }
    public static class OptimizationReviewStore
    {
        private static string StatePath => Path.Combine("ProjectSettings", "OptifunityReview.json");
        public static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string Hash(string value)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        public static string Key(PerformanceIssue issue) => Hash(issue.RuleId + "\n" + issue.Module + "\n" + (issue.AssetPath ?? "").Replace('\\','/') + "\n" + issue.CodeLocation + "\n" + issue.Title);
        public static FindingState Load()
        {
            if (!File.Exists(StatePath)) return new FindingState { project = Project };
            var state = JsonUtility.FromJson<FindingState>(File.ReadAllText(StatePath));
            if (state == null || state.version != 1 || state.project != Project || state.decisions == null || state.baseline == null)
                throw new InvalidDataException("Review state is invalid or belongs to another project.");
            return state;
        }
        public static void Save(FindingState state)
        {
            if (state == null || state.project != Project) throw new InvalidDataException("Wrong review project.");
            string temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(state, true));
            if (File.Exists(StatePath)) File.Replace(temp, StatePath, null); else File.Move(temp, StatePath);
        }
        public static void SetDecision(string key, bool suppressed, string reason, int cost, int impact)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(reason) || cost < 1 || cost > 3 || impact < 0 || impact > 3)
                throw new ArgumentException("Reason, cost 1-3 and estimated impact 0-3 are required.");
            var state = Load(); state.decisions.RemoveAll(x => x.key == key);
            state.decisions.Add(new FindingDecision { key = key, suppressed = suppressed, reason = reason.Trim(), cost = cost, impact = impact, updatedAt = DateTime.UtcNow.ToString("o") }); Save(state);
        }
        public static void SetBaseline(ScanReport report)
        {
            if (report == null || report.ModuleCoverage.Count == 0 || report.Completion.StartsWith("In progress") || report.ModuleCoverage.Any(c => c.Contains("Incomplete")))
                throw new InvalidOperationException("Capture a completed scoped report before setting a baseline.");
            var state = Load(); state.baseline = report.AllIssues.Select(Key).Distinct().ToList(); state.baselineCoverage = report.ModuleCoverage.ToList(); state.baselineAt = DateTime.UtcNow.ToString("o"); Save(state);
        }
        public static OptimizationReview Review(ScanReport report, FindingState state = null)
        {
            state ??= Load();
            var result = new OptimizationReview { project = Project, capturedAt = DateTime.UtcNow.ToString("o"),
                scope = report?.Scope ?? "No scan", completion = report?.Completion ?? "Unknown: no scan", moduleCoverage = report?.ModuleCoverage.ToList() ?? new List<string>() };
            result.baselineAt = state.baselineAt; result.baselineCoverage = state.baselineCoverage ?? new List<string>();
            if (report == null) return result;
            foreach (var finding in report.AllIssues)
            {
                string key = Key(finding); var decision = state.decisions.FirstOrDefault(x => x.key == key);
                var rule = RuleCatalog.Get(finding.RuleId);
                int evidence = rule.evidenceLevel == "Advisory" ? 0 : rule.evidenceLevel.StartsWith("Measured") ? 2 : 1;
                result.findings.Add(new PrioritizedFinding { key = key, finding = finding, rule = rule,
                    change = state.baseline.Contains(key) ? "Existing baseline finding" : "New or changed location",
                    suppressed = decision?.suppressed ?? false, reason = decision?.reason, cost = decision?.cost ?? 2, impact = decision?.impact ?? 0,
                    evidenceRank = evidence, priorityReason = "Evidence tier, user-estimated impact, then implementation cost; no predicted FPS gain. " + rule.tradeoffs });
            }
            result.findings = result.findings.OrderBy(x => x.suppressed).ThenByDescending(x => x.evidenceRank).ThenByDescending(x => x.impact).ThenBy(x => x.cost).ThenBy(x => x.key).ToList();
            result.absentBaselineKeys = state.baseline.Except(result.findings.Select(x => x.key)).ToList();
            return result;
        }
    }
}
