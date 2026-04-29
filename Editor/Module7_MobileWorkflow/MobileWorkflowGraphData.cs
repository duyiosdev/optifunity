using System;
using System.Collections.Generic;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module7
{
    public enum WorkflowVariant
    {
        Traditional,
        Unity6GRD
    }

    public enum MobileNodeStatus
    {
        Matched,
        Mismatched,
        Partial,
        Unknown,
        Advisory
    }

    public enum MobileNodeEvidenceLevel
    {
        ProjectSetting,
        URPAsset,
        AssetHeuristic,
        AdvisoryOnly
    }

    [Serializable]
    public class MobileWorkflowNode
    {
        public string Id;
        public string Title;
        public string Subtitle;
        public MobileNodeStatus Status;
        public MobileNodeEvidenceLevel EvidenceLevel;
        public string ActualState;
        public string CurrentValue;
        public string RecommendedValue;
        public string Suggestion;
        public IssueSeverity SeverityWhenMismatch;
        public string SourcePathHint;
    }

    [Serializable]
    public class MobileWorkflowEdge
    {
        public string FromId;
        public string ToId;
        public bool IsPrimaryPath;
    }

    [Serializable]
    public class MobileWorkflowGraphData
    {
        public WorkflowVariant Variant;
        public List<MobileWorkflowNode> Nodes = new();
        public List<MobileWorkflowEdge> Edges = new();
        public DateTime GeneratedAt;
    }
}
