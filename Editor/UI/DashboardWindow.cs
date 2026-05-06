using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module1;
using Optifunity.Editor.Module2;
using Optifunity.Editor.Module4;
using Optifunity.Editor.Module5;
using Optifunity.Editor.Module5.UI;
using Optifunity.Editor.Module6;
using Optifunity.Editor.Module7;
using Optifunity.Editor.Module8;

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

        private readonly string[] _tabNames = { "Overview", "Code", "Assets", "Shader/Materials", "Render Pipeline", "🧩 Scene", "🔬 Spike", "📦 Build" };

        // Config panel
        private bool _showConfig;

        // ─── Issue List Virtualization State ─────────────────────────────────
        // Cache grouped and sorted lists to avoid per-frame LINQ
        private List<IssueGroup> _cachedCodeIssues;
        private List<IssueGroup> _cachedAssetIssues;
        private List<IssueGroup> _cachedURPIssues;
        private List<IssueGroup> _cachedMobileIssues;
        private List<IssueGroup> _cachedSceneIssues;
        private ScanReport       _cachedForReport;   // invalidate when report changes

        private class IssueGroup
        {
            public string                Title;
            public string                Description;
            public string                FixSuggestion;
            public IssueSeverity         Severity;
            public IssueModule           Module; // Usually the same, but we group by title
            public List<PerformanceIssue> Instances = new();
            public bool                  IsExpanded = false;

            public bool CanAutoFix => Instances.Any(i => i.CanAutoFix && i.AutoFixAction != null);
        }

        // Scroll positions per-tab (for virtual scroll)
        private Vector2 _scrollCode;
        private Vector2 _scrollAssets;
        private Vector2 _scrollURP;
        private Vector2 _scrollMobile;
        private Vector2 _scrollOverview;
        private Vector2 _scrollBuild;
        private Vector2 _scrollScene;

        private BuildAssetScanner.BuildResults _buildResults;

        // Estimated height per issue row (measured once, updated lazily)
        private float _rowHeight = 68f;
        private const int   VIRTUAL_OVERSCAN  = 3;
        private const float VIRTUAL_MIN_ITEMS = 20;

        // ─── Severity Filter State ──────────────────────────────────────────────
        // 0 = All, 1 = Error only, 2 = Warning only, 3 = Info only
        private int _filterCode   = 0;
        private int _filterAssets = 0;
        private int _filterURP    = 0;
        private int _filterMobile = 0;
        private int _filterScene  = 0;
        private readonly string[] _filterLabels = { "All", "🛑 Error", "⚠️ Warning", "🔵 Info" };

        private int _mobilePipelineFilter = 0;
        private readonly string[] _mobilePipelineLabels = { "All", "GRD", "SRP", "GPU Instancing", "Static", "Dynamic", "Fallback" };
        private string _selectedMobilePipelineId;

        private readonly string[] _onOffLabels = { "On", "Off" };
        private bool _sceneFilterEnableMpb;
        private int _sceneFilterMpbExpected;
        private bool _sceneFilterEnableInstancing;
        private int _sceneFilterInstancingExpected;
        private bool _sceneFilterEnableStaticBatching;
        private int _sceneFilterStaticBatchingExpected;
        private bool _sceneFilterEnableLodGroup;
        private int _sceneFilterLodGroupExpected;
        private bool _sceneFilterEnableXrMotion;
        private int _sceneFilterXrMotionExpected;
        private bool _sceneFilterEnableDotsInstancing;
        private int _sceneFilterDotsInstancingExpected;
        private bool _sceneIncludeSkinnedMeshRenderers;

        // ─── Scan Mode ──────────────────────────────────────────────────────
        private bool _myScriptsOnly = false;

        // Logic for "My Scripts Only" mode: Check if path starts with any whitelisted folder
        public static bool IsUserCodePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            string normalized = assetPath.Replace('\\', '/');

            var whitelist = PlatformConfig.GetMyScriptsFoldersList();
            foreach (var folder in whitelist)
            {
                if (normalized.StartsWith(folder, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

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
                // Scan mode toggle
                bool prevScriptsOnly = _myScriptsOnly;
                _myScriptsOnly = GUILayout.Toggle(_myScriptsOnly,
                    _myScriptsOnly ? "📌 My Scripts" : "📌 My Scripts",
                    "Button", GUILayout.Width(100), GUILayout.Height(28));
                if (_myScriptsOnly != prevScriptsOnly)
                {
                    // Invalidate caches when scan mode changes
                    _cachedForReport = null;
                }

                GUILayout.Space(4);

                GUI.enabled = !_isScanning;
                // Full Scan button
                if (GUILayout.Button("▶  Full Scan", GUILayout.Width(120), GUILayout.Height(28)))
                    RunFullScan();

                // Individual module scan buttons
                if (GUILayout.Button("Code", GUILayout.Width(60), GUILayout.Height(28)))
                    RunCodeScan();
                if (GUILayout.Button("Assets", GUILayout.Width(65), GUILayout.Height(28)))
                    RunAssetScan();
                if (GUILayout.Button("URP", GUILayout.Width(55), GUILayout.Height(28)))
                    RunURPScan();
                if (GUILayout.Button("Scene", GUILayout.Width(65), GUILayout.Height(28)))
                    RunSceneScan(BuildSceneScanOptions());

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

            GUILayout.Space(10);
            GUILayout.Label("My Scripts — Inclusion Folders", OptifunityStyles.StyleHeader);
            var folders = PlatformConfig.GetMyScriptsFoldersList();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int toRemove = -1;
                for (int i = 0; i < folders.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($" • {folders[i]}", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                            toRemove = i;
                    }
                }

                if (toRemove >= 0)
                {
                    folders.RemoveAt(toRemove);
                    PlatformConfig.SetMyScriptsFoldersList(folders);
                }

                GUILayout.Space(4);
                if (GUILayout.Button("+ Add Folder", GUILayout.Width(100)))
                {
                    string path = EditorUtility.OpenFolderPanel("Select Folder to Include in Scan", "Assets", "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        // Convert to relative path
                        string projectPath = System.IO.Path.GetFullPath(Application.dataPath + "/..");
                        path = path.Replace('\\', '/').Replace(projectPath.Replace('\\', '/') + "/", "");

                        if (!folders.Contains(path))
                        {
                            folders.Add(path);
                            PlatformConfig.SetMyScriptsFoldersList(folders);
                        }
                    }
                }
            }

            GUILayout.Space(8);
        }

        private void DrawTabContent()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            GUILayout.Space(6);

            switch (_selectedTab)
            {
                case 0: DrawOverviewTab(); break;
                case 1: DrawVirtualIssueList(GetCachedIssues(0), "Code Analysis Issues",  "Chưa có dữ liệu — chạy Code Scan hoặc Full Scan",  ref _scrollCode,   ref _filterCode);   break;
                case 2: DrawVirtualIssueList(GetCachedIssues(1), "Asset Audit Issues",    "Chưa có dữ liệu — chạy Asset Scan hoặc Full Scan", ref _scrollAssets, ref _filterAssets); break;
                case 3: DrawVirtualIssueList(GetCachedIssues(2), "Shader/Materials",       "Chưa có dữ liệu — chạy URP Scan hoặc Full Scan",   ref _scrollURP,    ref _filterURP);    break;
                case 4: DrawMobileTab(); break;
                case 5: DrawSceneTab(); break;
                case 6: DrawSpikeTab(); break;
                case 7: DrawBuildAssetTab(); break;
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

            // Summary Cards
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSummaryCard("Code Analysis",  _lastReport.CodeIssues,    "📄");
                DrawSummaryCard("Asset Audit",    _lastReport.AssetIssues,   "🗂");
                DrawSummaryCard("Shader/Materials", _lastReport.URPIssues,          "🎨");
                DrawSummaryCard("Render Pipeline",  _lastReport.MobileIssues,       "📱");
                DrawSummaryCard("Scene Renderer",   _lastReport.SceneRendererIssues, "🧩");
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
                var topGroups = GroupAndSortIssues(topIssues);
                foreach (var group in topGroups)
                    DrawIssueGroupRow(group);
            }
        }

        private void DrawBuildAssetTab()
        {
            GUILayout.Label("📦 Trích xuất tài nguyên Build Cuối (Final Build Assets)", OptifunityStyles.StyleHeader);
            GUILayout.Space(5);
            GUILayout.Label("Phân tích dependency từ EditorBuildSettings, Resources và GraphicsSettings để tổng hợp Material/Shader có khả năng được include vào build (không phải danh sách đóng gói cuối sau stripping).",
                OptifunityStyles.StyleIssueDesc);
            GUILayout.Space(10);

            if (GUILayout.Button("🔍 Scan Build Assets", GUILayout.Width(150), GUILayout.Height(30)))
            {
                _buildResults = BuildAssetScanner.GetBuildShadersAndMaterials();
            }

            if (_buildResults == null)
            {
                GUILayout.Space(30);
                GUILayout.Label("Nhấn nút Scan ở trên để bắt đầu quẹt cây thư mục...", 
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                return;
            }

            GUILayout.Space(15);

            _scrollBuild = EditorGUILayout.BeginScrollView(_scrollBuild);
            
            using (new EditorGUILayout.HorizontalScope())
            {
                // Materials Column
                using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard, GUILayout.Width(position.width * 0.48f)))
                {
                    GUILayout.Label($"🎨 Materials ({_buildResults.Materials.Count})", OptifunityStyles.StyleIssueTitle);
                    OptifunityStyles.DrawSeparator();
                    foreach (var matPath in _buildResults.Materials)
                    {
                        _buildResults.MaterialSources.TryGetValue(matPath, out var src);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(matPath, EditorStyles.miniLabel);
                            GUILayout.Space(6);
                            if (!string.IsNullOrEmpty(src))
                            {
                                Color c = src == "Scene" ? new Color(0.45f, 0.8f, 1f) :
                                          src == "Resources" ? new Color(0.7f, 0.95f, 0.65f) :
                                          src == "Always Included Shader" ? new Color(1f, 0.75f, 0.35f) :
                                          OptifunityStyles.TextSecondary;
                                OptifunityStyles.DrawBadge(src, c);
                            }
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Ping", EditorStyles.miniButtonRight, GUILayout.Width(40)))
                            {
                                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(matPath));
                            }
                        }
                    }
                }

                GUILayout.FlexibleSpace();

                // Shaders Column
                using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard, GUILayout.Width(position.width * 0.48f)))
                {
                    GUILayout.Label($"⚙️ Shaders ({_buildResults.Shaders.Count})", OptifunityStyles.StyleIssueTitle);
                    OptifunityStyles.DrawSeparator();
                    foreach (var shaderPath in _buildResults.Shaders)
                    {
                        _buildResults.ShaderSources.TryGetValue(shaderPath, out var src);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(shaderPath, EditorStyles.miniLabel);
                            GUILayout.Space(6);
                            if (!string.IsNullOrEmpty(src))
                            {
                                Color c = src == "Scene" ? new Color(0.45f, 0.8f, 1f) :
                                          src == "Resources" ? new Color(0.7f, 0.95f, 0.65f) :
                                          src == "Always Included Shader" ? new Color(1f, 0.75f, 0.35f) :
                                          OptifunityStyles.TextSecondary;
                                OptifunityStyles.DrawBadge(src, c);
                            }
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Ping", EditorStyles.miniButtonRight, GUILayout.Width(40)))
                            {
                                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(shaderPath));
                            }
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
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

        // ─── Cached Sorted Issue Lists ────────────────────────────────────────
        private List<IssueGroup> GetCachedIssues(int kind)
        {
            // Invalidate whenever report changes
            if (_cachedForReport != _lastReport)
            {
                _cachedForReport = _lastReport;
                _cachedCodeIssues   = GroupAndSortIssues(_lastReport?.CodeIssues);
                _cachedAssetIssues  = GroupAndSortIssues(_lastReport?.AssetIssues);
                _cachedURPIssues    = GroupAndSortIssues(_lastReport?.URPIssues);
                _cachedMobileIssues = GroupAndSortIssues(_lastReport?.MobileIssues);
                _cachedSceneIssues  = GroupAndSortIssues(_lastReport?.SceneRendererIssues);
            }
            return kind switch
            {
                0 => _cachedCodeIssues,
                1 => _cachedAssetIssues,
                2 => _cachedURPIssues,
                3 => _cachedMobileIssues,
                _ => _cachedSceneIssues
            };
        }

        private static List<IssueGroup> GroupAndSortIssues(List<PerformanceIssue> raw)
        {
            if (raw == null || raw.Count == 0) return new List<IssueGroup>();

            // Group by Title + Severity
            var groups = raw.GroupBy(i => new { i.Title, i.Severity })
                .Select(g => new IssueGroup
                {
                    Title         = g.Key.Title,
                    Severity      = g.Key.Severity,
                    Description   = g.First().Description,
                    FixSuggestion = g.First().FixSuggestion,
                    Module        = g.First().Module,
                    Instances     = g.ToList()
                })
                .OrderByDescending(g => g.Severity)
                .ThenBy(g => g.Title)
                .ToList();

            return groups;
        }

        // ─── Virtualized Issue List ───────────────────────────────────────────
        private void DrawVirtualIssueList(
            List<IssueGroup> groups, string header, string emptyMsg,
            ref Vector2 scroll, ref int severityFilter)
        {
            GUILayout.Label($"  {header}", OptifunityStyles.StyleHeader);

            if (groups == null || groups.Count == 0)
            {
                GUILayout.Space(16);
                GUILayout.Label($"  {emptyMsg}",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                return;
            }

            // ── Summary bar + severity filter ──────────────────────────────────────
            int totalIssues = groups.Sum(g => g.Instances.Count);
            int errCnt  = groups.Sum(g => g.Instances.Count(i => i.Severity == IssueSeverity.Error));
            int warnCnt = groups.Sum(g => g.Instances.Count(i => i.Severity == IssueSeverity.Warning));
            int infoCnt = groups.Sum(g => g.Instances.Count(i => i.Severity == IssueSeverity.Info));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                GUILayout.Label($"{totalIssues} issues in {groups.Count} groups:", OptifunityStyles.StyleSubtitle, GUILayout.Width(160));
                OptifunityStyles.DrawBadge($"🛑 {errCnt}",   OptifunityStyles.ColorError);
                GUILayout.Space(4);
                OptifunityStyles.DrawBadge($"⚠️ {warnCnt}",  OptifunityStyles.ColorWarning);
                GUILayout.Space(4);
                OptifunityStyles.DrawBadge($"🔵 {infoCnt}",   OptifunityStyles.ColorInfo);
                GUILayout.FlexibleSpace();

                // Filter buttons (right side)
                GUILayout.Label("Filter:", OptifunityStyles.StyleSubtitle, GUILayout.Width(42));
                int newFilter = GUILayout.Toolbar(severityFilter, _filterLabels,
                    GUILayout.Height(20), GUILayout.Width(260));
                if (newFilter != severityFilter)
                {
                    severityFilter = newFilter;
                    scroll = Vector2.zero; // reset scroll on filter change
                }
                GUILayout.Space(8);
            }
            GUILayout.Space(4);

            // Apply filter
            var filtered = severityFilter switch
            {
                1 => groups.Where(g => g.Severity == IssueSeverity.Error).ToList(),
                2 => groups.Where(g => g.Severity == IssueSeverity.Warning).ToList(),
                3 => groups.Where(g => g.Severity == IssueSeverity.Info).ToList(),
                _ => groups
            };

            if (filtered.Count == 0)
            {
                GUILayout.Space(10);
                GUILayout.Label($"  Không có issue nào ở mức \"{_filterLabels[severityFilter]}\".",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                return;
            }

            // Virtualized rendering
            if (filtered.Count <= VIRTUAL_MIN_ITEMS)
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (var group in filtered) DrawIssueGroupRow(group);
                GUILayout.Space(8);
                EditorGUILayout.EndScrollView();
                return;
            }

            float viewportH  = position.height - 200f;
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(viewportH));

            int firstVis = Mathf.Max(0, (int)(scroll.y / _rowHeight) - VIRTUAL_OVERSCAN);
            int lastVis  = Mathf.Min(filtered.Count - 1,
                            (int)((scroll.y + viewportH) / _rowHeight) + VIRTUAL_OVERSCAN);

            if (firstVis > 0) GUILayout.Space(firstVis * _rowHeight);

            for (int i = firstVis; i <= lastVis; i++)
            {
                DrawIssueGroupRow(filtered[i]);
                if (i == firstVis && Event.current.type == EventType.Repaint)
                {
                    var r = GUILayoutUtility.GetLastRect();
                    if (r.height > 10f) _rowHeight = Mathf.Lerp(_rowHeight, r.height, 0.2f);
                }
            }

            int rem = filtered.Count - 1 - lastVis;
            if (rem > 0) GUILayout.Space(rem * _rowHeight);

            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();
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

            var grouped = GroupAndSortIssues(issues);
            foreach (var group in grouped)
                DrawIssueGroupRow(group);
        }

        private void DrawIssueGroupRow(IssueGroup group)
        {
            Texture2D bg = OptifunityStyles.GetSeverityBg(group.Severity);
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
                    string icon  = OptifunityStyles.GetSeverityIcon(group.Severity);
                    var iconStyle = OptifunityStyles.GetSeverityStyle(group.Severity);
                    GUILayout.Label(icon, iconStyle, GUILayout.Width(14));

                    // Title
                    GUILayout.Label(group.Title, OptifunityStyles.StyleIssueTitle);
                    
                    // Count badge
                    if (group.Instances.Count > 1)
                    {
                        GUILayout.Space(5);
                        OptifunityStyles.DrawBadge($"{group.Instances.Count} files", OptifunityStyles.TextPrimary);
                    }

                    GUILayout.FlexibleSpace();

                    // Module badge
                    OptifunityStyles.DrawBadge(group.Module.ToString(), OptifunityStyles.TextSecondary);

                    // Fix All button
                    if (group.CanAutoFix)
                    {
                        if (GUILayout.Button("⚡ Fix All",
                            OptifunityStyles.StyleButtonAutoFix, GUILayout.Width(90)))
                        {
                            if (EditorUtility.DisplayDialog("Optifunity — Fix All",
                                $"Tự động sửa '{group.Title}' cho tất cả {group.Instances.Count} file?\n\nBạn có chắc chắn?", "Sửa Tất Cả", "Hủy"))
                            {
                                int fixCount = 0;
                                foreach (var inst in group.Instances)
                                {
                                    if (inst.CanAutoFix && inst.AutoFixAction != null)
                                    {
                                        inst.AutoFixAction();
                                        fixCount++;
                                    }
                                }

                                if (_lastReport != null)
                                {
                                    List<PerformanceIssue> targetList = group.Module switch
                                    {
                                        IssueModule.CodeAnalysis      => _lastReport.CodeIssues,
                                        IssueModule.AssetAudit        => _lastReport.AssetIssues,
                                        IssueModule.URPDiagnostics    => _lastReport.URPIssues,
                                        IssueModule.MobileWorkflow    => _lastReport.MobileIssues,
                                        IssueModule.SceneRendererAudit=> _lastReport.SceneRendererIssues,
                                        _                             => null
                                    };

                                    if (targetList != null)
                                    {
                                        targetList.RemoveAll(i =>
                                            i.Module == group.Module &&
                                            i.Severity == group.Severity &&
                                            i.Title == group.Title);
                                    }

                                    _cachedForReport = null;
                                }

                                _statusMsg = $"Fixed {fixCount} issues: {group.Title}";
                                Repaint();
                            }
                        }
                    }
                }

                // Description & Fix
                if (!string.IsNullOrEmpty(group.Description))
                {
                    GUILayout.Label(group.Description, OptifunityStyles.StyleIssueDesc);
                }

                if (!string.IsNullOrEmpty(group.FixSuggestion))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("💡 ", GUILayout.Width(16));
                        GUILayout.Label(group.FixSuggestion,
                            new GUIStyle(OptifunityStyles.StyleIssueDesc)
                            {
                                normal = { textColor = new Color(0.7f, 0.85f, 0.65f) }
                            });
                    }
                }

                // File List section
                GUILayout.Space(4);
                OptifunityStyles.DrawSeparator(0.3f);
                GUILayout.Space(2);
                
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // Only show first 5, then toggle button if many
                    int showCount = group.IsExpanded ? group.Instances.Count : Mathf.Min(group.Instances.Count, 5);
                    for (int i = 0; i < showCount; i++)
                    {
                        var inst = group.Instances[i];
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            string hierarchyPath = null;
                            bool hasSceneObjectPath = inst.Module == IssueModule.SceneRendererAudit &&
                                SceneRendererAuditRunner.TryExtractSceneGameObjectPath(inst.CodeLocation, out _, out hierarchyPath);

                            string pathDisplay = hasSceneObjectPath
                                ? hierarchyPath
                                : (inst.Module == IssueModule.SceneRendererAudit
                                    ? "[Missing GameObject token]"
                                    : (string.IsNullOrEmpty(inst.AssetPath) ? inst.CodeLocation : inst.AssetPath));

                            if (string.IsNullOrEmpty(pathDisplay)) pathDisplay = "Global / Project Settings";
                            if (pathDisplay.Length > 100) pathDisplay = "..." + pathDisplay.Substring(pathDisplay.Length - 97);

                            GUILayout.Label($"• {pathDisplay}", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();

                            if (inst.Module == IssueModule.SceneRendererAudit)
                            {
                                if (GUILayout.Button("LOD", GUILayout.Width(38), GUILayout.Height(16)))
                                {
                                    bool locatedLod = SceneRendererAuditRunner.TryLocateReferencingLodGroup(inst);
                                    if (!locatedLod)
                                        EditorUtility.DisplayDialog("Optifunity", "Không tìm thấy LODGroup đang reference renderer này.", "OK");
                                }
                            }

                            if (GUILayout.Button("→", GUILayout.Width(22), GUILayout.Height(16)))
                            {
                                if (inst.Module == IssueModule.SceneRendererAudit)
                                {
                                    bool located = SceneRendererAuditRunner.TryLocateGameObject(inst);
                                    if (!located)
                                        EditorUtility.DisplayDialog("Optifunity", "Không locate được GameObject từ issue này. Hãy chạy lại Scene Scan để refresh token object path.", "OK");
                                }
                                else if (!string.IsNullOrEmpty(inst.AssetPath))
                                {
                                    CodeAnalysisRunner.PingAsset(inst.AssetPath);
                                }
                                else if (!string.IsNullOrEmpty(inst.CodeLocation) && !inst.CodeLocation.StartsWith("scenego://"))
                                {
                                    CodeAnalysisRunner.PingAsset(inst.CodeLocation.Split(':')[0]);
                                }
                            }
                        }
                    }

                    if (group.Instances.Count > 5)
                    {
                        var clickStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                        clickStyle.hover.textColor = new Color(0.4f, 0.6f, 1f, 1f); // Accent blue on hover
                        
                        if (!group.IsExpanded)
                        {
                            if (GUILayout.Button($"... and {group.Instances.Count - 5} more files ▼", clickStyle))
                                group.IsExpanded = true;
                        }
                        else
                        {
                            if (GUILayout.Button("Shrink list ▲", clickStyle))
                                group.IsExpanded = false;
                        }
                    }
                }
            }
        }

        private void DrawSceneTab()
        {
            GUILayout.Label("  🧩 Scene Renderer Audit", OptifunityStyles.StyleHeader);

            var issues = _lastReport?.SceneRendererIssues ?? new List<PerformanceIssue>();
            var groups = GetCachedIssues(4) ?? new List<IssueGroup>();
            int total = issues.Count;
            int tokenOk = issues.Count(i => i.Module == IssueModule.SceneRendererAudit &&
                                            SceneRendererAuditRunner.TryExtractSceneGameObjectPath(i.CodeLocation, out _, out _));
            int tokenMissing = total - tokenOk;

            using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard))
            {
                GUILayout.Label("Scene Filters", OptifunityStyles.StyleIssueTitle);

                DrawSceneFilterRow("MaterialPropertyBlock", ref _sceneFilterEnableMpb, ref _sceneFilterMpbExpected);
                DrawSceneFilterRow("GPU Instancing", ref _sceneFilterEnableInstancing, ref _sceneFilterInstancingExpected);
                DrawSceneFilterRow("Static Batching", ref _sceneFilterEnableStaticBatching, ref _sceneFilterStaticBatchingExpected);
                DrawSceneFilterRow("LODGroup", ref _sceneFilterEnableLodGroup, ref _sceneFilterLodGroupExpected);
                DrawSceneFilterRow("XR Motion", ref _sceneFilterEnableXrMotion, ref _sceneFilterXrMotionExpected);
                DrawSceneFilterRow("DOTS Instancing Support", ref _sceneFilterEnableDotsInstancing, ref _sceneFilterDotsInstancingExpected);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _sceneIncludeSkinnedMeshRenderers = EditorGUILayout.ToggleLeft("Scan Skinned Mesh Renderer", _sceneIncludeSkinnedMeshRenderers, GUILayout.Width(220));
                    GUILayout.FlexibleSpace();
                }

                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(8);
                    GUILayout.Label($"{total} issues in {groups.Count} groups", OptifunityStyles.StyleSubtitle, GUILayout.Width(180));
                    OptifunityStyles.DrawBadge($"Token OK: {tokenOk}", OptifunityStyles.ColorSuccess);
                    GUILayout.Space(4);
                    OptifunityStyles.DrawBadge($"Token Missing: {tokenMissing}", tokenMissing > 0 ? OptifunityStyles.ColorWarning : OptifunityStyles.ColorInfo);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Run Scene Scan", GUILayout.Width(110), GUILayout.Height(22)))
                        RunSceneScan(BuildSceneScanOptions());
                    GUILayout.Space(8);
                }
            }

            if (issues.Count == 0)
            {
                GUILayout.Space(12);
                GUILayout.Label("  Chưa có dữ liệu — chọn filter rồi bấm Run Scene Scan.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                return;
            }

            DrawVirtualIssueList(groups, "Scene Renderer Audit", "Chưa có dữ liệu — chạy Scene Scan hoặc Full Scan", ref _scrollScene, ref _filterScene);
        }


        private void DrawMobileTab()
        {
            GUILayout.Label("  📱 Mobile Render Pipeline", OptifunityStyles.StyleHeader);

            var report = MobileWorkflowRunner.LastPipelineReport;
            if (report == null || report.Pipelines == null || report.Pipelines.Count == 0)
            {
                using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard))
                {
                    GUILayout.Label("Chưa có dữ liệu pipeline.", OptifunityStyles.StyleIssueTitle);
                    GUILayout.Label("Chạy Full Scan để đánh giá GRD, SRP Batcher, GPU Instancing, Static Batching, Dynamic Batching và Fallback.", OptifunityStyles.StyleIssueDesc);
                }

                GUILayout.Space(6);
                DrawVirtualIssueList(GetCachedIssues(3), "Mobile Render Pipeline", "Chưa có dữ liệu — chạy Full Scan để phân tích Render Pipeline", ref _scrollMobile, ref _filterMobile);
                return;
            }

            using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard))
            {
                GUILayout.Label("Pipeline Systems — parallel per renderer", OptifunityStyles.StyleIssueTitle);
                GUILayout.Label("Unity đánh giá từng Renderer độc lập; các hệ thống tối ưu có thể hoạt động song song thay vì một workflow tuyến tính.", OptifunityStyles.StyleIssueDesc);
                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Filter:", OptifunityStyles.StyleSubtitle, GUILayout.Width(50));
                    _mobilePipelineFilter = GUILayout.Toolbar(_mobilePipelineFilter, _mobilePipelineLabels, GUILayout.Height(22));
                }
            }

            var pipelines = FilterMobilePipelines(report.Pipelines);
            if (pipelines.Count == 0)
                pipelines = report.Pipelines;

            if (string.IsNullOrEmpty(_selectedMobilePipelineId) || pipelines.All(p => p.Id != _selectedMobilePipelineId))
                _selectedMobilePipelineId = pipelines[0].Id;

            using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard))
            {
                GUILayout.Label("Pipeline Cards", OptifunityStyles.StyleIssueTitle);
                GUILayout.Space(4);

                int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 60f) / 250f));
                for (int i = 0; i < pipelines.Count; i += columns)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int c = 0; c < columns && i + c < pipelines.Count; c++)
                            DrawMobilePipelineCard(pipelines[i + c]);
                        GUILayout.FlexibleSpace();
                    }
                }
            }

            var selected = report.Pipelines.FirstOrDefault(p => p.Id == _selectedMobilePipelineId) ?? pipelines[0];
            DrawMobilePipelineDetails(selected);

            GUILayout.Space(6);
            DrawVirtualIssueList(GetCachedIssues(3), "Mobile Render Pipeline Issues", "Chưa có dữ liệu — chạy Full Scan để phân tích Render Pipeline", ref _scrollMobile, ref _filterMobile);
        }

        private List<MobileRenderPipelineSystem> FilterMobilePipelines(List<MobileRenderPipelineSystem> pipelines)
        {
            if (_mobilePipelineFilter <= 0) return pipelines;
            string id = _mobilePipelineFilter switch
            {
                1 => "grd",
                2 => "srp",
                3 => "instancing",
                4 => "static",
                5 => "dynamic",
                6 => "fallback",
                _ => null
            };
            return pipelines.Where(p => p.Id == id).ToList();
        }

        private void DrawMobilePipelineCard(MobileRenderPipelineSystem pipeline)
        {
            Color badgeColor = GetMobileStatusColor(pipeline.Status);
            bool isSelected = _selectedMobilePipelineId == pipeline.Id;
            var cardStyle = new GUIStyle(OptifunityStyles.StyleCard)
            {
                margin = new RectOffset(3, 3, 3, 3),
                normal = { background = isSelected ? OptifunityStyles.MakeTexture(badgeColor * 0.18f) : OptifunityStyles.StyleCard.normal.background }
            };

            using (new EditorGUILayout.VerticalScope(cardStyle, GUILayout.Width(240), GUILayout.MinHeight(150)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(pipeline.Title, OptifunityStyles.StyleIssueTitle);
                    GUILayout.FlexibleSpace();
                    OptifunityStyles.DrawBadge(pipeline.Status.ToString(), badgeColor);
                }

                GUILayout.Label(pipeline.Subtitle, OptifunityStyles.StyleIssueDesc);
                GUILayout.Space(3);
                GUILayout.Label(pipeline.Goal, OptifunityStyles.StyleIssueDesc);
                GUILayout.Space(4);

                int matched = pipeline.Checks.Count(c => c.Status == MobileNodeStatus.Matched);
                int mismatch = pipeline.Checks.Count(c => c.Status == MobileNodeStatus.Mismatched);
                int advisory = pipeline.Checks.Count(c => c.Status == MobileNodeStatus.Advisory || c.Status == MobileNodeStatus.Partial || c.Status == MobileNodeStatus.Unknown);
                using (new EditorGUILayout.HorizontalScope())
                {
                    OptifunityStyles.DrawBadge($"✓ {matched}", OptifunityStyles.ColorSuccess);
                    GUILayout.Space(3);
                    OptifunityStyles.DrawBadge($"! {mismatch}", mismatch > 0 ? OptifunityStyles.ColorError : OptifunityStyles.TextSecondary);
                    GUILayout.Space(3);
                    OptifunityStyles.DrawBadge($"i {advisory}", OptifunityStyles.ColorInfo);
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button(isSelected ? "Selected" : "Inspect", GUILayout.Height(20)))
                    _selectedMobilePipelineId = pipeline.Id;
            }
        }

        private void DrawMobilePipelineDetails(MobileRenderPipelineSystem pipeline)
        {
            using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"Pipeline Details: {pipeline.Title}", OptifunityStyles.StyleIssueTitle);
                    GUILayout.FlexibleSpace();
                    OptifunityStyles.DrawBadge(pipeline.Status.ToString(), GetMobileStatusColor(pipeline.Status));
                }

                GUILayout.Label($"Flow: {pipeline.RenderFlow}", OptifunityStyles.StyleIssueDesc);
                GUILayout.Label($"Use case: {pipeline.BestUseCase}", OptifunityStyles.StyleIssueDesc);
                GUILayout.Label($"Compatibility: {pipeline.CompatibilityNotes}", OptifunityStyles.StyleIssueDesc);
                GUILayout.Label($"Recommendation: {pipeline.Recommendation}", new GUIStyle(OptifunityStyles.StyleIssueDesc)
                {
                    normal = { textColor = new Color(0.75f, 0.9f, 0.7f) }
                });

                GUILayout.Space(6);
                GUILayout.Label("Setting Checks", OptifunityStyles.StyleSubtitle);

                foreach (var check in pipeline.Checks)
                    DrawMobileSettingCheck(check);
            }
        }

        private void DrawMobileSettingCheck(MobilePipelineSettingCheck check)
        {
            Color statusColor = GetMobileStatusColor(check.Status);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    OptifunityStyles.DrawBadge(check.Status.ToString(), statusColor);
                    GUILayout.Label(check.Title, OptifunityStyles.StyleIssueTitle, GUILayout.Width(230));
                    OptifunityStyles.DrawBadge(check.SourceKind.ToString(), OptifunityStyles.TextSecondary);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Locate", GUILayout.Width(70), GUILayout.Height(18)))
                        LocateMobileSetting(check);
                }

                GUILayout.Label($"Current: {check.ActualValue}", OptifunityStyles.StyleIssueDesc);
                GUILayout.Label($"Recommended: {check.RecommendedValue}", OptifunityStyles.StyleIssueDesc);
                GUILayout.Label($"💡 {check.Description}", new GUIStyle(OptifunityStyles.StyleIssueDesc)
                {
                    normal = { textColor = new Color(0.75f, 0.9f, 0.7f) }
                });
                GUILayout.Label($"Locate Hint: {check.LocateHint}", OptifunityStyles.StyleIssueDesc);
            }
        }

        private Color GetMobileStatusColor(MobileNodeStatus status)
        {
            return status switch
            {
                MobileNodeStatus.Matched => OptifunityStyles.ColorSuccess,
                MobileNodeStatus.Mismatched => OptifunityStyles.ColorError,
                MobileNodeStatus.Partial => OptifunityStyles.ColorWarning,
                MobileNodeStatus.Unknown => OptifunityStyles.TextSecondary,
                _ => OptifunityStyles.ColorInfo
            };
        }

        private void LocateMobileSetting(MobilePipelineSettingCheck check)
        {
            if (check == null) return;

            switch (check.LocateKey)
            {
                case "urp":
                    URPAssetScanner.PingURPAsset();
                    return;
                case "player":
                    SettingsService.OpenProjectSettings("Project/Player");
                    return;
                case "graphics":
                    SettingsService.OpenProjectSettings("Project/Graphics");
                    return;
                case "quality":
                    SettingsService.OpenProjectSettings("Project/Quality");
                    return;
                case "scene":
                    _selectedTab = 5;
                    Repaint();
                    EditorUtility.DisplayDialog("Optifunity", "Đã chuyển sang tab Scene Renderer Audit. Chạy Scene Scan để kiểm tra renderer/material cụ thể.", "OK");
                    return;
                case "frame_debugger":
                    EditorUtility.DisplayDialog("Optifunity", "Mở Window > Analysis > Frame Debugger để xác nhận path thực tế: DrawMeshInstancedIndirect = GRD, SRP Batcher = CPU optimized path, DrawMeshInstanced = GPU Instancing, Draw Call = Fallback.", "OK");
                    return;
                case "material":
                    EditorUtility.DisplayDialog("Optifunity", "Setting này nằm trên Material/Shader asset. Hãy mở material mục tiêu và kiểm tra Enable GPU Instancing hoặc shader compatibility.", "OK");
                    return;
                default:
                    EditorUtility.DisplayDialog("Optifunity", check.LocateHint ?? "Không map được setting cụ thể.", "OK");
                    return;
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
            string mode = _myScriptsOnly ? " [My Scripts]" : "";
            _statusMsg  = $"Đang scan{mode}...";
            Repaint();

            try
            {
                ReportEngine.BeginScan();

                _statusMsg = $"Phân tích Code{mode}...";
                Repaint();
                ReportEngine.AddIssues(IssueModule.CodeAnalysis,
                    CodeAnalysisRunner.RunAll(myScriptsOnly: _myScriptsOnly));

                _statusMsg = $"Kiểm toán Assets{mode}...";
                Repaint();
                ReportEngine.AddIssues(IssueModule.AssetAudit,
                    AssetAuditRunner.RunAll(myScriptsOnly: _myScriptsOnly));

                _statusMsg = "Quét URP...";
                Repaint();
                RunURPScanInternal();

                _statusMsg = "Phân tích Mobile Workflow...";
                Repaint();
                RunMobileScanInternal();

                _statusMsg = "Phân tích Scene Renderer...";
                Repaint();
                RunSceneScanInternal(BuildSceneScanOptions());

                ReportEngine.FinalizeScan();
                _lastReport = ReportEngine.LastReport;
                _cachedForReport = null; // force cache rebuild
                _statusMsg = $"Full Scan hoàn tất{mode} — {_lastReport.TotalCount} issues, Health: {_lastReport.HealthScore}/100";
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
            var issues = CodeAnalysisRunner.RunAll(myScriptsOnly: _myScriptsOnly);
            ReportEngine.AddIssues(IssueModule.CodeAnalysis, issues);
            ReportEngine.FinalizeScan();
            _lastReport = ReportEngine.LastReport;
            _cachedForReport = null;
            _selectedTab = 1;
            string mode = _myScriptsOnly ? " [My Scripts]" : "";
            _statusMsg = $"Code Scan{mode}: {issues.Count} issues";
            Repaint();
        }

        private void RunAssetScan()
        {
            ReportEngine.BeginScan();
            var issues = AssetAuditRunner.RunAll(myScriptsOnly: _myScriptsOnly);
            ReportEngine.AddIssues(IssueModule.AssetAudit, issues);
            ReportEngine.FinalizeScan();
            _lastReport = ReportEngine.LastReport;
            _cachedForReport = null;
            _selectedTab = 2;
            string mode = _myScriptsOnly ? " [My Scripts]" : "";
            _statusMsg = $"Asset Scan{mode}: {issues.Count} issues";
            Repaint();
        }

        private void RunURPScan()
        {
            ReportEngine.BeginScan();
            RunURPScanInternal();
            ReportEngine.FinalizeScan();
            _lastReport = ReportEngine.LastReport;
            _selectedTab = 3;
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

        private void RunMobileScanInternal()
        {
            var mobileIssues = MobileWorkflowRunner.RunAll();
            ReportEngine.AddIssues(IssueModule.MobileWorkflow, mobileIssues);
            _statusMsg = $"Mobile Workflow Scan: {mobileIssues.Count} issues";
        }

        private void RunSceneScan(SceneMeshRendererScanner.ScanOptions options = null)
        {
            ReportEngine.BeginScan();
            RunSceneScanInternal(options);
            ReportEngine.FinalizeScan();
            _lastReport = ReportEngine.LastReport;
            _cachedForReport = null;
            _selectedTab = 5;
            Repaint();
        }

        private void RunSceneScanInternal(SceneMeshRendererScanner.ScanOptions options = null)
        {
            var sceneIssues = SceneRendererAuditRunner.RunAll(options);
            ReportEngine.AddIssues(IssueModule.SceneRendererAudit, sceneIssues);
            _statusMsg = $"Scene Scan: {sceneIssues.Count} issues";
        }

        private void DrawSceneFilterRow(string label, ref bool enabled, ref int expected)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                enabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(18));
                GUILayout.Label(label, GUILayout.Width(180));
                using (new EditorGUI.DisabledScope(!enabled))
                {
                    expected = GUILayout.Toolbar(expected, _onOffLabels, GUILayout.Width(140));
                }
                GUILayout.FlexibleSpace();
            }
        }

        private SceneMeshRendererScanner.ScanOptions BuildSceneScanOptions()
        {
            return new SceneMeshRendererScanner.ScanOptions
            {
                IncludeSkinnedMeshRenderers = _sceneIncludeSkinnedMeshRenderers,

                EnableMaterialPropertyBlockFilter = _sceneFilterEnableMpb,
                MaterialPropertyBlockExpectedOn = _sceneFilterMpbExpected == 0,

                EnableGpuInstancingFilter = _sceneFilterEnableInstancing,
                GpuInstancingExpectedOn = _sceneFilterInstancingExpected == 0,

                EnableStaticBatchingFilter = _sceneFilterEnableStaticBatching,
                StaticBatchingExpectedOn = _sceneFilterStaticBatchingExpected == 0,

                EnableLodGroupFilter = _sceneFilterEnableLodGroup,
                LodGroupExpectedOn = _sceneFilterLodGroupExpected == 0,

                EnableXrMotionFilter = _sceneFilterEnableXrMotion,
                XrMotionExpectedOn = _sceneFilterXrMotionExpected == 0,

                EnableDotsInstancingFilter = _sceneFilterEnableDotsInstancing,
                DotsInstancingExpectedOn = _sceneFilterDotsInstancingExpected == 0
            };
        }
    }
}
