using System.Collections.Generic;
using Optifunity.Editor.Core;
namespace Optifunity.Editor.Module1
{
    public static class BoxingAnalyzer
    {
        public static List<PerformanceIssue> Analyze(string filePath, string[] lines) =>
            SemanticCodeAnalyzer.Analyze(filePath, lines, "Boxing");
    }
}
