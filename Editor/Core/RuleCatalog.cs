using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Optifunity.Editor.Core
{
    [Serializable] public class KnowledgeRule
    {
        public string ruleId, category, verifiedAt, verificationStatus, applicability, exclusions;
        public string requiredEvidence, severity, evidenceLevel, recommendation, tradeoffs, verificationSteps, fixPolicy, tests;
        public int revision;
        public string[] sources;
    }
    public static class RuleCatalog
    {
        [Serializable] private class Document { public KnowledgeRule[] rules; }
        private static Dictionary<string, KnowledgeRule> rules;
        public static void Validate(KnowledgeRule rule)
        {
            if (rule == null || rule.revision < 1) throw new InvalidDataException("Invalid rule revision.");
            foreach (var field in typeof(KnowledgeRule).GetFields())
                if (field.FieldType == typeof(string) && string.IsNullOrWhiteSpace((string)field.GetValue(rule)))
                    throw new InvalidDataException("Missing rule field: " + field.Name);
            if (!DateTime.TryParseExact(rule.verifiedAt, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _)) throw new InvalidDataException("Invalid verification date.");
            if (rule.fixPolicy != "Manual only" || rule.sources == null ||
                (rule.evidenceLevel != "Advisory" && rule.sources.Length == 0))
                throw new InvalidDataException("Unsupported fix policy or missing evidence sources.");
            foreach (var source in rule.sources)
                if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                    throw new InvalidDataException("Invalid source URL.");
        }
        public static KnowledgeRule[] All() { Get("legacy.unverified"); return new List<KnowledgeRule>(rules.Values).ToArray(); }
        public static KnowledgeRule Get(string id)
        {
            if (rules == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(RuleCatalog).Assembly);
                if (package == null) throw new InvalidOperationException("Install Optifunity as a UPM package to load its knowledge catalog.");
                var document = JsonUtility.FromJson<Document>(File.ReadAllText(Path.Combine(package.resolvedPath, "knowledge/rules.json")));
                var loaded = new Dictionary<string, KnowledgeRule>();
                if (document?.rules == null) throw new InvalidDataException("Missing rule catalog.");
                foreach (var rule in document.rules)
                {
                    Validate(rule);
                    if (loaded.ContainsKey(rule.ruleId)) throw new InvalidDataException("Duplicate rule metadata.");
                    loaded.Add(rule.ruleId, rule);
                }
                if (!loaded.ContainsKey("legacy.unverified")) throw new InvalidDataException("Missing advisory fallback.");
                rules = loaded;
            }
            return rules.TryGetValue(id ?? "", out var value) ? value : rules["legacy.unverified"];
        }
    }
}
