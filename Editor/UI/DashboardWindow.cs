using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module1;
using Optifunity.Editor.Module2;
using Optifunity.Editor.Module3;
using Optifunity.Editor.Module4;
using Optifunity.Editor.Module5;
using Optifunity.Editor.Module5.UI;

namespace Optifunity.Editor.UI
{
    /// <summary>
    /// Cửa sổ Dashboard chính của Optifunity.
    /// Menu: Tools > Optifunity > Dashboard (Ctrl+Shift+O)
    /// Hiển thị: Health Score, 4 module tabs, Quick Actions, Config panel.
    /// </summary>
    public class DashboardWindow : EditorWindow
    {
        // ─── State ────────────────────────────────────────────────────────────
        private int         _selectedTab;
        private Vector2     _scrollPos;
        private ScanReport  _lastReport;
        private bool        _isScanning;
        private string      _statusMsg = "Sẵn sàng. Nhấn 'Full Scan' để bắt đầu.";

        private readonly string[] _tabNames = { "Overview", "Code", "Assets", "Memory", "URP", "🔬 Spike" };

        // Config panel
        private bool _showConfig;

        // ─── Menu item ────────────────────────────────────────────────────────
        [MenuItem("Tools/Optifunity/Dashboard #&o", priority = 1)]
        public static void ShowWindow()
        {
            var window = GetWindow<DashboardWindow>("Optifunity");
            window.minSize = new Vector2(700, 500);
            window.Show();
        }

        [MenuItem("Tools/Optifunity/Run Full Scan", priority = 100)]
        public static void QuickScan()
        {
            var window = GetWindow<DashboardWindow>("Optifunity");
            window.RunFullScan();
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────
        private void OnEnable()
        {
            _lastReport = ReportEngine.LastReport;
        }

        private void OnDisable()
        {
            EditorUtility.ClearProgressBar();
        }

        // ─── Draw ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            // Background
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), OptifunityStyles.BgDark);

            DrawHeader();
            DrawToolbar();
            DrawTabContent();
            DrawStatusBar();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Space(10);
                    GUILayout.Label("⚡ Optifunity", OptifunityStyles.StyleTitle);
                    GUILayout.Label("Unity URP Performance Analysis Plugin", OptifunityStyles.StyleSubtitle);
                }

                GUILayout.FlexibleSpace();

                // Health Score
                if (_lastReport != null)
                {
                    int score = _lastReport.HealthScore;
                    Color scoreColor = OptifunityStyles.GetHealthColor(score);
                    GUILayout.BeginVertical(GUILayout.Width(80));
                    GUILayout.Space(8);
                    var scoreStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize  = 28,
                        alignment = TextAnchor.MiddleCenter,
                        normal    = { textColor = scoreColor }
                    };
                    GUILayout.Label($"{score}", scoreStyle, GUILayout.Width(80));
                    var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal    = { textColor = OptifunityStyles.TextSecondary }
                    };
                    GUILayout.Label("Health Score", labelStyle, GUILayout.Width(80));
                    GUILayout.EndVertical();
                }

                GUILayout.Space(8);
            }

            OptifunityStyles.DrawSeparator();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);

                // Full Scan button
                GUI.enabled = !_isScanning;
                if (GUILayout.Button("▶  Full Scan", GUILayout.Width(120), GUILayout.Height(28)))
                    RunFullScan();

                // Individual module scan buttons
                if (GUILayout.Button("Code", GUILayout.Width(60), GUILayout.Height(28)))
                    RunCodeScan();
                if (GUILayout.Button("Assets", GUILayout.Width(65), GUILayout.Height(28)))
                    RunAssetScan();
                if (GUILayout.Button("URP", GUILayout.Width(55), GUILayout.Height(28)))
                    RunURPScan();

                GUI.enabled = true;

                GUILayout.FlexibleSpace();

                // Config toggle
                _showConfig = GUILayout.Toggle(_showConfig, "⚙ Config", "Button",
                    GUILayout.Width(75), GUILayout.Height(28));

                // Export
                if (_lastReport != null &&
                    GUILayout.Button("⬇ Export", GUILayout.Width(75), GUILayout.Height(28)))
                {
                    ReportEngine.SaveMarkdownReport();
                    EditorUtility.DisplayDialog("Optifunity", "Báo cáo đã lưu vào Assets/Optifunity/Reports/", "OK");
                }

                GUILayout.Space(8);
            }

            // Config panel (collapsible)
            if (_showConfig) DrawConfigPanel();

            // Tab bar
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames, GUILayout.Height(24));
                GUILayout.Space(8);
            }

            OptifunityStyles.DrawSeparator();
        }

        private void DrawConfigPanel()
        {
            using var scope = new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard);
            GUILayout.Space(4);
            GUILayout.Label("Configuration", OptifunityStyles.StyleHeader);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Target Platform:", GUILayout.Width(130));
                PlatformConfig.ActivePlatform = (Runtime.TargetPlatformType)EditorGUILayout.EnumPopup(
                    PlatformConfig.ActivePlatform, GUILayout.Width(120));

                GUILayout.Space(20);

                GUILayout.Label("Android RAM (MB):", GUILayout.Width(130));
                int[] ramOptions  = { 1024, 2048, 3072, 4096, 6144, 8192 };
                string[] ramLabels = { "1GB", "2GB", "3GB", "4GB", "6GB", "8GB" };
                int curIdx = System.Array.IndexOf(ramOptions, PlatformConfig.AndroidPhysicalRamMB);
                curIdx = Mathf.Max(0, curIdx);
                int newIdx = EditorGUILayout.Popup(curIdx, ramLabels, GUILayout.Width(80));
                PlatformConfig.AndroidPhysicalRamMB = ramOptions[newIdx];
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Auto-Fix Enabled:", GUILayout.Width(130));
                PlatformConfig.AutoFixEnabled = EditorGUILayout.Toggle(PlatformConfig.AutoFixEnabled, GUILayout.Width(20));

                GUILayout.Space(20);

                GUILayout.Label("Audio Stream Threshold (s):", GUILayout.Width(190));
                PlatformConfig.AudioStreamingThresholdSeconds = EditorGUILayout.Slider(
                    PlatformConfig.AudioStreamingThresholdSeconds, 1f, 30f, GUILayout.Width(150));
            }

            // Budget summary
            var budget = PlatformConfig.GetBudgetSummary();
            using (new EditorGUILayout.HorizontalScope())
            {
                void DrawBudgetLabel(string label, string value)
                {
                    GUILayout.Label($"{label}: ", OptifunityStyles.StyleSubtitle, GUILayout.Width(70));
                    GUILayout.Label(value, new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 10, normal = { textColor = OptifunityStyles.AccentBlue }
                    }, GUILayout.Width(80));
                }
                DrawBudgetLabel("Available", $"{budget.AvailableBudgetMB:F0}MB");
                DrawBudgetLabel("Textures", $"{budget.TextureBudgetMB:F0}MB");
                DrawBudgetLabel("Meshes", $"{budget.MeshBudgetMB:F0}MB");
                DrawBudgetLabel("Heap", $"{budget.ManagedHeapBudgetMB:F0}MB");
                DrawBudgetLabel("ASTC", budget.RecommendedASTCBlock);
            }

            GUILayout.Space(4);
        }

        private void DrawTabContent()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            GUILayout.Space(6);

            switch (_selectedTab)
            {
                case 0: DrawOverviewTab(); break;
                case 1: DrawIssueList(_lastReport?.CodeIssues,   "Code Analysis Issues",  "Chưa có dữ liệu — chạy Code Scan hoặc Full Scan"); break;
                case 2: DrawIssueList(_lastReport?.AssetIssues,  "Asset Audit Issues",    "Chưa có dữ liệu — chạy Asset Scan hoặc Full Scan"); break;
                case 3: DrawMemoryTab(); break;
                case 4: DrawIssueList(_lastReport?.URPIssues,    "URP Diagnostics",       "Chưa có dữ liệu — chạy URP Scan hoặc Full Scan"); break;
                case 5: DrawSpikeTab(); break;
            }

            GUILayout.Space(10);
            EditorGUILayout.EndScrollView();
        }

        private void DrawOverviewTab()
        {
            if (_lastReport == null)
            {
                GUILayout.Space(40);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label("Chưa có dữ liệu scan.",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                    GUILayout.Space(8);
                    GUILayout.Label("Nhấn 'Full Scan' để phân tích toàn bộ project.",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                }
                return;
            }

            // Summary Cards (5 modules)
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSummaryCard("Code Analysis",  _lastReport.CodeIssues,   "📄");
                DrawSummaryCard("Asset Audit",    _lastReport.AssetIssues,  "🗂");
                DrawSummaryCard("Memory",         _lastReport.MemoryIssues, "💾");
                DrawSummaryCard("URP",            _lastReport.URPIssues,    "🎨");
            }

            GUILayout.Space(4);

            // Spike Analyzer teaser card
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard, GUILayout.ExpandWidth(true)))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("🔬  Spike Analyzer", OptifunityStyles.StyleIssueTitle);
                        GUILayout.FlexibleSpace();

                        var lastSpike = SpikeAnalysisRunner.LastAnalysis;
                        if (lastSpike != null)
                        {
                            OptifunityStyles.DrawBadge(
                                $"Last: Frame #{lastSpike.FrameIndex} — {lastSpike.PrimaryBottleneck}",
                                lastSpike.IsSpike ? OptifunityStyles.ColorError : OptifunityStyles.ColorSuccess);
                        }

                        if (GUILayout.Button("Open Spike Analyzer", GUILayout.Width(140), GUILayout.Height(20)))
                            SpikeAnalyzerWindow.ShowWindow();
                    }
                    GUILayout.Label(
                        "Tự động phân tích bất kỳ frame nào trong Profiler — click vào spike frame để xem kết quả.",
                        OptifunityStyles.StyleIssueDesc);
                    var lineRect2 = GUILayoutUtility.GetRect(0, 3, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(lineRect2, OptifunityStyles.AccentPurple);
                }
            }

            GUILayout.Space(8);
            OptifunityStyles.DrawSeparator();

            // Top priority issues (Errors first)
            GUILayout.Label("  Ưu Tiên Hàng Đầu — Errors & Critical Warnings",
                OptifunityStyles.StyleHeader);

            var topIssues = _lastReport.AllIssues
                .Where(i => i.Severity == IssueSeverity.Error)
                .Take(10)
                .ToList();

            if (topIssues.Count == 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("  ✓ Không có lỗi nghiêm trọng (Error). Kiểm tra Warnings trong các tab cụ thể.",
                    new GUIStyle(EditorStyles.label) { normal = { textColor = OptifunityStyles.ColorSuccess } });
            }
            else
            {
                foreach (var issue in topIssues)
                    DrawIssueRow(issue);
            }
        }

        private void DrawSummaryCard(string title, List<PerformanceIssue> issues, string icon)
        {
            int errors   = issues?.Count(i => i.Severity == IssueSeverity.Error)   ?? 0;
            int warnings = issues?.Count(i => i.Severity == IssueSeverity.Warning) ?? 0;

            Color cardColor = errors > 0 ? OptifunityStyles.ColorError :
                              warnings > 0 ? OptifunityStyles.ColorWarning :
                              OptifunityStyles.ColorSuccess;

            using (var scope = new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard,
                GUILayout.ExpandWidth(true)))
            {
                GUILayout.Label($"{icon}  {title}", OptifunityStyles.StyleIssueTitle);
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    OptifunityStyles.DrawBadge($"{errors} ERR", OptifunityStyles.ColorError);
                    OptifunityStyles.DrawBadge($"{warnings} WARN", OptifunityStyles.ColorWarning);
                    int info = issues?.Count(i => i.Severity == IssueSeverity.Info) ?? 0;
                    OptifunityStyles.DrawBadge($"{info} INFO", OptifunityStyles.ColorInfo);
                }
                GUILayout.Space(4);
                // Colored accent bar
                var lineRect = GUILayoutUtility.GetRect(0, 3, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(lineRect, cardColor);
            }
        }

        private void DrawIssueList(List<PerformanceIssue> issues, string header, string emptyMsg)
        {
            GUILayout.Label($"  {header}", OptifunityStyles.StyleHeader);

            if (issues == null || issues.Count == 0)
            {
                GUILayout.Space(16);
                GUILayout.Label($"  {emptyMsg}",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                return;
            }

            var sorted = issues.OrderByDescending(i => i.Severity).ToList();
            foreach (var issue in sorted)
                DrawIssueRow(issue);
        }

        private void DrawIssueRow(PerformanceIssue issue)
        {
            Texture2D bg = OptifunityStyles.GetSeverityBg(issue.Severity);
            var rowStyle = new GUIStyle(OptifunityStyles.StyleCard)
            {
                normal = { background = bg },
                margin = new RectOffset(8, 8, 2, 2)
            };

            using (new EditorGUILayout.VerticalScope(rowStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Severity icon
                    string icon  = OptifunityStyles.GetSeverityIcon(issue.Severity);
                    var iconStyle = OptifunityStyles.GetSeverityStyle(issue.Severity);
                    GUILayout.Label(icon, iconStyle, GUILayout.Width(14));

                    // Title
                    GUILayout.Label(issue.Title, OptifunityStyles.StyleIssueTitle);
                    GUILayout.FlexibleSpace();

                    // Module badge
                    OptifunityStyles.DrawBadge(issue.Module.ToString(), OptifunityStyles.TextSecondary);

                    // Auto-Fix button
                    if (issue.CanAutoFix && issue.AutoFixAction != null)
                    {
                        if (GUILayout.Button(issue.AutoFixLabel ?? "Auto-Fix",
                            OptifunityStyles.StyleButtonAutoFix, GUILayout.Width(90)))
                        {
                            if (EditorUtility.DisplayDialog("Optifunity — Auto-Fix",
                                $"Tự động sửa:\n{issue.Title}\n\nBạn có chắc chắn?", "Sửa", "Hủy"))
                            {
                                issue.AutoFixAction();
                                _statusMsg = $"Auto-Fix applied: {issue.Title}";
                            }
                        }
                    }

                    // Ping asset
                    if (!string.IsNullOrEmpty(issue.AssetPath))
                    {
                        if (GUILayout.Button("→", GUILayout.Width(22), GUILayout.Height(18)))
                            CodeAnalysisRunner.PingAsset(issue.AssetPath);
                    }
                }

                // Description & Fix (collapsible via title click — simplified to always show)
                if (!string.IsNullOrEmpty(issue.Description))
                {
                    GUILayout.Label(issue.Description, OptifunityStyles.StyleIssueDesc);
                }

                if (!string.IsNullOrEmpty(issue.FixSuggestion))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("💡 ", GUILayout.Width(16));
                        GUILayout.Label(issue.FixSuggestion,
                            new GUIStyle(OptifunityStyles.StyleIssueDesc)
                            {
                                normal = { textColor = new Color(0.7f, 0.85f, 0.65f) }
                            });
                    }
                }

                if (!string.IsNullOrEmpty(issue.CodeLocation))
                    GUILayout.Label($"@ {issue.CodeLocation}", OptifunityStyles.StyleSubtitle);
            }
        }

        private void DrawSpikeTab()
        {
            GUILayout.Label("  🔬 Spike Analyzer", OptifunityStyles.StyleHeader);

            var lastReport = SpikeAnalysisRunner.LastAnalysis;

            // Status card
            using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool profilerHasData = ProfilerDriver.firstFrameIndex >= 0 &&
                                          ProfilerDriver.lastFrameIndex  >= 0;

                    GUILayout.Label(
                        profilerHasData
                            ? $"✓ Profiler có dữ liệu — frames {ProfilerDriver.firstFrameIndex}...{ProfilerDriver.lastFrameIndex}"
                            : "⚠ Profiler chưa có dữ liệu. Mở Profiler window và bắt đầu record.",
                        new GUIStyle(EditorStyles.boldLabel)
                        {
                            normal = { textColor = profilerHasData
                                ? OptifunityStyles.ColorSuccess
                                : OptifunityStyles.ColorWarning }
                        });

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("🔬 Open Full Spike Analyzer",
                        GUILayout.Width(200), GUILayout.Height(26)))
                        SpikeAnalyzerWindow.ShowWindow();
                }

                GUILayout.Label(
                    "Chức năng: Tự động phân tích bottleneck khi click bất kỳ frame nào trong Profiler.\n" +
                    "Click vào frame spike (thanh đỏ) trong Profiler Timeline → Optifunity tự động phân tích và hiển thị kết quả.",
                    OptifunityStyles.StyleIssueDesc);
            }

            GUILayout.Space(6);

            if (lastReport == null)
            {
                GUILayout.Space(20);
                GUILayout.Label(
                    "  Chưa có frame nào được phân tích.\n" +
                    "  Mở Spike Analyzer window và click vào frame bất kỳ trong Unity Profiler.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                return;
            }

            // Last analysis summary
            GUILayout.Label("  Kết Quả Phân Tích Gần Nhất", OptifunityStyles.StyleHeader);

            Color spikeColor = lastReport.IsSpike ? OptifunityStyles.ColorError : OptifunityStyles.ColorSuccess;

            using (new EditorGUILayout.VerticalScope(new GUIStyle(OptifunityStyles.StyleCard)
                   { normal = { background = OptifunityStyles.MakeTexture(spikeColor * 0.15f) },
                     margin = new RectOffset(8, 8, 2, 2) }))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"Frame #{lastReport.FrameIndex}",
                        new GUIStyle(EditorStyles.boldLabel)
                        { fontSize = 13, normal = { textColor = OptifunityStyles.AccentBlue } },
                        GUILayout.Width(100));

                    GUILayout.Label($"{lastReport.FrameTotalMs:F2}ms",
                        new GUIStyle(EditorStyles.boldLabel)
                        { fontSize = 13, normal = { textColor = spikeColor } },
                        GUILayout.Width(75));

                    if (lastReport.IsSpike)
                        OptifunityStyles.DrawBadge($"⚠ SPIKE {lastReport.Severity}", spikeColor);
                    else
                        OptifunityStyles.DrawBadge("✓ Normal", spikeColor);

                    GUILayout.Space(6);
                    OptifunityStyles.DrawBadge(lastReport.PrimaryBottleneck.ToString(),
                        OptifunityStyles.AccentPurple);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Xem Chi Tiết →", GUILayout.Width(110)))
                        SpikeAnalyzerWindow.ShowAndAnalyze(lastReport.FrameIndex);
                }

                GUILayout.Space(4);
                GUILayout.Label(lastReport.PrimaryTitle, OptifunityStyles.StyleIssueTitle);
                GUILayout.Label(lastReport.PrimaryDescription, OptifunityStyles.StyleIssueDesc);

                if (lastReport.Recommendations.Count > 0)
                {
                    GUILayout.Space(4);
                    OptifunityStyles.DrawSeparator(0.5f);
                    GUILayout.Label("💡 Top Recommendation:",
                        new GUIStyle(OptifunityStyles.StyleIssueDesc)
                        { normal = { textColor = new Color(0.7f, 0.9f, 0.65f) } });
                    GUILayout.Label($"  {lastReport.Recommendations[0]}",
                        new GUIStyle(OptifunityStyles.StyleIssueDesc)
                        { normal = { textColor = new Color(0.75f, 0.9f, 0.7f) } });
                }
            }

            // Contributing factors summary
            if (lastReport.ContributingFactors.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("  Contributing Factors:", OptifunityStyles.StyleSubtitle);
                foreach (var cf in lastReport.ContributingFactors.Take(3))
                {
                    using (new EditorGUILayout.HorizontalScope(OptifunityStyles.StyleCard))
                    {
                        GUILayout.Label($"  ◆ {cf.Type}",
                            new GUIStyle(EditorStyles.label)
                            { normal = { textColor = OptifunityStyles.ColorWarning }, fontSize = 10 },
                            GUILayout.Width(180));
                        GUILayout.Label($"{cf.Confidence * 100:F0}% | {cf.TimeMs:F2}ms",
                            OptifunityStyles.StyleIssueDesc, GUILayout.Width(110));
                        GUILayout.Label(cf.Fix, OptifunityStyles.StyleIssueDesc);
                    }
                }
            }
        }

        private void DrawMemoryTab()
        {
            GUILayout.Label("  Memory Profiler & Budget Validator", OptifunityStyles.StyleHeader);

            // Snapshot controls
            using (var scope = new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard,
                GUILayout.Width(position.width - 20)))
            {
                GUILayout.Label("Memory Snapshot Controls", OptifunityStyles.StyleIssueTitle);
                GUILayout.Space(4);

                bool memProfilerAvailable = SnapshotCapturer.IsMemoryProfilerAvailable();
                if (!memProfilerAvailable)
                {
                    EditorGUILayout.HelpBox(
                        "Memory Profiler package chưa được cài đặt.\n" +
                        "Cài qua: Window > Package Manager > com.unity.memoryprofiler >= 1.1.0",
                        MessageType.Warning);
                }

                GUI.enabled = memProfilerAvailable && Application.isPlaying;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("📷 Baseline Snapshot", GUILayout.Height(26)))
                        SnapshotCapturer.TakeSnapshot(SnapshotCapturer.SnapshotType.Baseline);
                    if (GUILayout.Button("📷 Peak Load Snapshot", GUILayout.Height(26)))
                        SnapshotCapturer.TakeSnapshot(SnapshotCapturer.SnapshotType.PeakLoad);
                    if (GUILayout.Button("📷 Teardown Snapshot", GUILayout.Height(26)))
                        SnapshotCapturer.TakeSnapshot(SnapshotCapturer.SnapshotType.Teardown);
                }
                GUI.enabled = true;

                if (!Application.isPlaying)
                    GUILayout.Label("  ⚠ Memory Snapshot chỉ khả dụng trong Play Mode",
                        new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = OptifunityStyles.ColorWarning } });
            }

            GUILayout.Space(6);
            DrawIssueList(_lastReport?.MemoryIssues, "Memory Analysis Results",
                "Chưa có dữ liệu — chụp Snapshots hoặc chạy Full Scan");
        }

        private void DrawStatusBar()
        {
            var rect = new Rect(0, position.height - 22, position.width, 22);
            EditorGUI.DrawRect(rect, OptifunityStyles.BgHeader);
            GUI.Label(new Rect(8, position.height - 20, position.width - 100, 20),
                _statusMsg,
                new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = OptifunityStyles.TextSecondary }
                });

            if (_lastReport != null)
            {
                GUI.Label(new Rect(position.width - 200, position.height - 20, 200, 20),
                    $"Scan: {_lastReport.ScanTime:HH:mm:ss} | {_lastReport.TotalCount} issues",
                    new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        normal    = { textColor = OptifunityStyles.TextSecondary }
                    });
            }
        }

        // ─── Scan Actions ─────────────────────────────────────────────────────

        private void RunFullScan()
        {
            _isScanning = true;
            _statusMsg  = "Đang scan...";
            Repaint();

            try
            {
                var report = ReportEngine.BeginScan();

                _statusMsg = "Phân tích Code...";
                Repaint();
                ReportEngine.AddIssues(IssueModule.CodeAnalysis, CodeAnalysisRunner.RunAll());

                _statusMsg = "Kiểm toán Assets...";
                Repaint();
                ReportEngine.AddIssues(IssueModule.AssetAudit, AssetAuditRunner.RunAll());

                _statusMsg = "Quét URP...";
                Repaint();
                RunURPScanInternal();

                _statusMsg = "Validate Memory Budget...";
                Repaint();
                ReportEngine.AddIssues(IssueModule.MemoryProfiler, MemoryBudgetValidator.Validate());

                ReportEngine.FinalizeScan();
                _lastReport = ReportEngine.LastReport;
                _statusMsg  = $"Full Scan hoàn tất — {_lastReport.TotalCount} issues, Health: {_lastReport.HealthScore}/100";
            }
            finally
            {
                _isScanning = false;
                Repaint();
            }
        }

        private void RunCodeScan()
        {
            ReportEngine.BeginScan();
            var issues = CodeAnalysisRunner.RunAll();
            ReportEngine.AddIssues(IssueModule.CodeAnalysis, issues);
            ReportEngine.FinalizeScan();
            _lastReport = ReportEngine.LastReport;
            _selectedTab = 1;
            _statusMsg = $"Code Scan: {issues.Count} issues";
            Repaint();
        }

        private void RunAssetScan()
        {
            ReportEngine.BeginScan();
            var issues = AssetAuditRunner.RunAll();
            ReportEngine.AddIssues(IssueModule.AssetAudit, issues);
            ReportEngine.FinalizeScan();
            _lastReport = ReportEngine.LastReport;
            _selectedTab = 2;
            _statusMsg = $"Asset Scan: {issues.Count} issues";
            Repaint();
        }

        private void RunURPScan()
        {
            ReportEngine.BeginScan();
            RunURPScanInternal();
            ReportEngine.FinalizeScan();
            _lastReport = ReportEngine.LastReport;
            _selectedTab = 4;
            Repaint();
        }

        private void RunURPScanInternal()
        {
            var snapshot    = URPAssetScanner.Scan();
            var urpIssues   = URPRecommendationEngine.Analyze(snapshot);
            var srpIssues   = SRPBatcherChecker.CheckAllShaders();
            var drawIssues  = DrawCallAnalyzer.Analyze();

            var allURP = new List<PerformanceIssue>();
            allURP.AddRange(urpIssues);
            allURP.AddRange(srpIssues);
            allURP.AddRange(drawIssues);

            ReportEngine.AddIssues(IssueModule.URPDiagnostics, allURP);
            _statusMsg = $"URP Scan: {allURP.Count} issues";
        }
    }
}
