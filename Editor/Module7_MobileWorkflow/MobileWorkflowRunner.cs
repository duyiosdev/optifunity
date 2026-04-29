using System.Collections.Generic;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module4;

namespace Optifunity.Editor.Module7
{
    public static class MobileWorkflowRunner
    {
        public static MobileWorkflowGraphData LastTraditionalGraph { get; private set; }
        public static MobileWorkflowGraphData LastUnity6GrdGraph { get; private set; }

        public static List<PerformanceIssue> RunAll(bool showProgress = true)
        {
            var all = new List<PerformanceIssue>();

            var snapshot = URPAssetScanner.Scan();
            LastTraditionalGraph = MobileWorkflowGraphBuilder.BuildTraditional(snapshot);
            LastUnity6GrdGraph = MobileWorkflowGraphBuilder.BuildUnity6Grd(snapshot);

            all.AddRange(MobileWorkflowAnalyzer.Analyze(snapshot));
            all.AddRange(ProjectSettingsMismatchAnalyzer.Analyze(snapshot));
            all.AddRange(Unity6VulkanGuidanceAnalyzer.Analyze());

            return all;
        }
    }
}
