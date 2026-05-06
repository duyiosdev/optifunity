using System;
using System.Collections.Generic;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module7
{
    public enum WorkflowVariant
    {
        PipelineSystems,
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

    public enum MobileSettingSourceKind
    {
        URPAsset,
        PlayerSettings,
        GraphicsSettings,
        QualitySettings,
        MaterialAsset,
        SceneAudit,
        FrameDebugger,
        Advisory
    }

    [Serializable]
    public class MobilePipelineSettingCheck
    {
        public string Id;
        public string Title;
        public MobileNodeStatus Status;
        public MobileSettingSourceKind SourceKind;
        public string ActualValue;
        public string RecommendedValue;
        public string Description;
        public string LocateHint;
        public string LocateKey;
        public IssueSeverity SeverityWhenMismatch;
    }

    [Serializable]
    public class MobileRenderPipelineSystem
    {
        public string Id;
        public string Title;
        public string Subtitle;
        public string Goal;
        public string RenderFlow;
        public string BestUseCase;
        public string CompatibilityNotes;
        public string Recommendation;
        public MobileNodeStatus Status;
        public IssueSeverity SeverityWhenMismatch;
        public List<MobilePipelineSettingCheck> Checks = new();
    }

    [Serializable]
    public class MobileRenderPipelineReport
    {
        public DateTime GeneratedAt;
        public List<MobileRenderPipelineSystem> Pipelines = new();
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
