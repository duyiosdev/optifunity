using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module2
{
    /// <summary>
    /// Điều phối toàn bộ Asset Auditing:
    /// Chạy TextureAuditor + MeshAuditor + AudioAuditor, thu thập tổng hợp.
    /// </summary>
    public static class AssetAuditRunner
    {
        private static bool _isRunning;
        public static string LastCoverage { get; private set; } = "Not scanned";

        public static List<PerformanceIssue> RunAll(bool showProgress = true, bool myScriptsOnly = false)
        {
            if (_isRunning)
            {
                Debug.LogWarning("[Optifunity] Asset audit đang chạy.");
                return new List<PerformanceIssue>();
            }

            _isRunning = true;
            LastCoverage = "Incomplete: asset audit started";
            var all = new List<PerformanceIssue>();

            try
            {
                if (showProgress)
                    EditorUtility.DisplayProgressBar("Optifunity — Asset Audit", "Kiểm toán Textures...", 0.1f);

                var texIssues = TextureAuditor.Audit(myScriptsOnly);
                all.AddRange(texIssues);

                if (showProgress)
                    EditorUtility.DisplayProgressBar("Optifunity — Asset Audit", "Kiểm toán Meshes...", 0.5f);

                var meshIssues = MeshAuditor.Audit(myScriptsOnly);
                all.AddRange(meshIssues);

                if (showProgress)
                    EditorUtility.DisplayProgressBar("Optifunity — Asset Audit", "Kiểm toán Audio...", 0.8f);

                var audioIssues = AudioAuditor.Audit(myScriptsOnly);
                all.AddRange(audioIssues);
                LastCoverage = TextureAuditor.LastCoverage + "; " + MeshAuditor.LastCoverage + "; " + AudioAuditor.LastCoverage;
            }
            finally
            {
                _isRunning = false;
                if (showProgress) EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[Optifunity] Asset Audit hoàn tất: {all.Count} issues.");
            return all;
        }
    }
}
