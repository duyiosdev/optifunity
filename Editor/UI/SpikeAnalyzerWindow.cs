using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Optifunity.Editor.UI;
using Optifunity.Editor.Module5;

namespace Optifunity.Editor.Module5.UI
{
    /// <summary>
    /// Cửa sổ phân tích Spike chuyên dụng.
    /// Tự động cập nhật khi người dùng click vào bất kỳ frame nào trong Unity Profiler.
    /// Menu: Tools > Optifunity > Spike Analyzer
    /// </summary>
    public class SpikeAnalyzerWindow : EditorWindow
    {
        // ─── State ────────────────────────────────────────────────────────────
        private SpikeAnalysisReport _currentReport;
        private IReadOnlyList<FrameTimeData> _timeline;

        private int    _compareFrameIndex = -1;   // Frame thứ hai để so sánh
        private SpikeAnalysisReport _compareReport;

        private Vector2 _scrollReport;

        private bool _autoFollowProfiler = true;
        private float _spikeThreshold = SpikeDetector.DEFAULT_SPIKE_FACTOR;

        // Timeline chart state
        private Rect  _timelineRect;
        private int   _hoveredFrameIndex = -1;

        // Cache locate-path để tránh FindAssets/AssetDatabase lặp lại mỗi repaint
        private readonly Dictionary<string, string> _samplePathCache = new();
        private readonly Dictionary<int, Texture2D> _cardTextureCache = new();

        // Tabs trong Analysis panel
        private const int TAB_BOTTLENECK = 0;
        private const int TAB_ROOT_CAUSE = 1;
        private const int TAB_SAMPLES    = 2;
        private const int TAB_GC_ALLOC   = 3;
        private const int TAB_RENDERING  = 4;
        private const int TAB_COMPARE    = 5;

        private int _analysisTab;
        private readonly string[] _analysisTabs =
            { "Bottleneck", "Root Cause", "Samples", "GC Alloc", "Rendering", "Compare" };

        // ─── Menu ─────────────────────────────────────────────────────────────
        [MenuItem("Tools/Optifunity/Spike Analyzer", priority = 10)]
        public static void ShowWindow()
        {
            var win = GetWindow<SpikeAnalyzerWindow>("Optifunity — Spike Analyzer");
            win.minSize = new Vector2(800, 560);
            win.Show();
        }

        public static void ShowAndAnalyze(int frameIndex)
        {
            var win = GetWindow<SpikeAnalyzerWindow>("Optifunity — Spike Analyzer");
            win.minSize = new Vector2(800, 560);
            win.AnalyzeFrame(frameIndex);
            win.Show();
        }

        // ─── Lifecycle ─────────────────────────────────────────────────────────
        private void OnEnable()
        {
            SpikeDetector.OnFrameSelected   += OnProfilerFrameSelected;
            SpikeDetector.OnTimelineUpdated += OnTimelineUpdated;

            // Force immediate load of ALL available frames when window opens
            int first = ProfilerDriver.firstFrameIndex;
            int last  = ProfilerDriver.lastFrameIndex;
            if (first >= 0 && last >= 0)
            {
                SpikeDetector.BulkLoadTimeline(first, last);
            }

            _timeline = SpikeDetector.FrameBuffer;
            _samplePathCache.Clear();
            EditorCodeLocator.Initialize();

            // Analyze currently selected frame if ProfilerWindow is open
            int curFrame = ProfilerHelper.GetSelectedFrame();
            if (curFrame >= 0) AnalyzeFrame(curFrame);
        }

        private void OnDisable()
        {
            SpikeDetector.OnFrameSelected   -= OnProfilerFrameSelected;
            SpikeDetector.OnTimelineUpdated -= OnTimelineUpdated;

            foreach (var tex in _cardTextureCache.Values)
            {
                if (tex != null) DestroyImmediate(tex);
            }
            _cardTextureCache.Clear();
            _samplePathCache.Clear();
        }

        private void OnProfilerFrameSelected(int frameIndex, bool isSpike)
        {
            if (!_autoFollowProfiler) return;
            AnalyzeFrame(frameIndex);
            Repaint();
        }

        private void OnTimelineUpdated(IReadOnlyList<FrameTimeData> buffer)
        {
            _timeline = buffer;
            Repaint();
        }

        // Polling ~10x/s to catch selection changes even without an event
        private void OnInspectorUpdate()
        {
            if (!_autoFollowProfiler) return;
            int cur = ProfilerHelper.GetSelectedFrame();
            // cur == -1 means Profiler window is not open — skip
            if (cur < 0) return;
            if (_currentReport == null || cur != _currentReport.FrameIndex)
            {
                AnalyzeFrame(cur);
                Repaint();
            }
        }

        private void AnalyzeFrame(int frameIndex)
        {
            _currentReport = SpikeAnalysisRunner.AnalyzeFrame(frameIndex);
            Repaint();
        }

        private string GetCachedSamplePath(string sampleName)
        {
            if (string.IsNullOrEmpty(sampleName)) return null;
            if (_samplePathCache.TryGetValue(sampleName, out var cached)) return cached;

            var path = EditorCodeLocator.FindScriptPath(sampleName);
            _samplePathCache[sampleName] = path;
            return path;
        }

        // ─── Draw ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height),
                OptifunityStyles.BgDark);

            DrawHeader();
            DrawTimeline();
            OptifunityStyles.DrawSeparator();
            DrawAnalysisPanel();
        }

        // ─── Header ───────────────────────────────────────────────────────────
        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                GUILayout.Label("🔬 Spike Analyzer", OptifunityStyles.StyleTitle);
                GUILayout.Label("— Tự động phân tích khi chọn frame trong Profiler",
                    OptifunityStyles.StyleSubtitle);
                GUILayout.FlexibleSpace();

                // Auto-follow toggle
                var prevColor = GUI.contentColor;
                GUI.contentColor = _autoFollowProfiler
                    ? OptifunityStyles.ColorSuccess
                    : OptifunityStyles.TextSecondary;
                _autoFollowProfiler = GUILayout.Toggle(_autoFollowProfiler,
                    _autoFollowProfiler ? "● Auto-Follow ON" : "○ Auto-Follow OFF",
                    "Button", GUILayout.Width(130), GUILayout.Height(26));
                GUI.contentColor = prevColor;

                GUILayout.Space(4);

                // Spike threshold slider
                GUILayout.Label("Spike ×", GUILayout.Width(50));
                _spikeThreshold = EditorGUILayout.Slider(_spikeThreshold, 1.2f, 4.0f,
                    GUILayout.Width(100));
                SpikeDetector.SpikeFactor = _spikeThreshold;

                GUILayout.Space(4);

                if (GUILayout.Button("⟳ Clear Cache", GUILayout.Width(90), GUILayout.Height(26)))
                {
                    SpikeAnalysisRunner.ClearCache();
                    SpikeDetector.Reset();
                    _samplePathCache.Clear();
                    _currentReport = null;
                    _compareReport = null;
                }

                GUILayout.Space(8);
            }

            OptifunityStyles.DrawSeparator();
        }

        // ─── Timeline Chart ────────────────────────────────────────────────────
        private void DrawTimeline()
        {
            const float CHART_HEIGHT = 80f;
            const float CHART_MARGIN = 8f;

            GUILayout.Space(4);
            GUILayout.Label("  Frame Timeline — click để phân tích frame",
                OptifunityStyles.StyleSubtitle);

            // Reserve rect for chart
            _timelineRect = GUILayoutUtility.GetRect(
                position.width - CHART_MARGIN * 2,
                CHART_HEIGHT,
                GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(_timelineRect, new Color(0.1f, 0.12f, 0.15f));

            if (_timeline == null || _timeline.Count == 0)
            {
                int first = ProfilerDriver.firstFrameIndex;
                int last  = ProfilerDriver.lastFrameIndex;
                bool hasData = first >= 0 && last >= 0;

                string msg = hasData
                    ? $"  Đang tải dữ liệu... ({last - first + 1} frames khả dụng)"
                    : "  Chưa có dữ liệu Profiler.\n  1. Mở: Window → Analysis → Profiler\n  2. Nhấn Record (đƲn tròn đỏ) và chạy game\n  3. Click vào bất kỳ frame nào trong Profiler";

                GUI.Label(_timelineRect, msg,
                    new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal    = { textColor = hasData
                            ? OptifunityStyles.ColorWarning
                            : OptifunityStyles.TextSecondary },
                        alignment = TextAnchor.MiddleCenter,
                        wordWrap  = true
                    });

                // If data exists but not loaded yet, trigger bulk load
                if (hasData)
                    SpikeDetector.BulkLoadTimeline(first, last);
                return;
            }

            // Tính max giá trị để normalize bars
            float maxMs = 0;
            for (int i = 0; i < _timeline.Count; i++)
                if (_timeline[i].TotalMs > maxMs) maxMs = _timeline[i].TotalMs;
            if (maxMs < 1f) maxMs = 16.67f;

            // Constant reference lines (16.67ms = 60fps, 33.33ms = 30fps)
            DrawRefLine(_timelineRect, 16.67f, maxMs, new Color(0.3f, 0.8f, 0.3f, 0.4f), "60fps");
            DrawRefLine(_timelineRect, 33.33f, maxMs, new Color(1.0f, 0.6f, 0.0f, 0.4f), "30fps");

            float barWidth = Mathf.Max(1f, _timelineRect.width / _timeline.Count);

            for (int i = 0; i < _timeline.Count; i++)
            {
                var fdata  = _timeline[i];
                float barH = (fdata.TotalMs / maxMs) * (_timelineRect.height - 6f);
                float bx   = _timelineRect.x + i * barWidth;
                float by   = _timelineRect.y + _timelineRect.height - barH;

                // Bar color
                Color barColor;
                bool isSelected = _currentReport != null && fdata.FrameIndex == _currentReport.FrameIndex;
                bool isHovered  = fdata.FrameIndex == _hoveredFrameIndex;

                if (isSelected)
                    barColor = OptifunityStyles.AccentBlue;
                else if (isHovered)
                    barColor = Color.white * 0.8f;
                else if (fdata.IsSpike)
                    barColor = OptifunityStyles.ColorError;
                else if (fdata.TotalMs > 16.67f)
                    barColor = OptifunityStyles.ColorWarning;
                else
                    barColor = OptifunityStyles.ColorSuccess;

                if (barH > 0.5f)
                    EditorGUI.DrawRect(new Rect(bx, by, Mathf.Max(1f, barWidth - 0.5f), barH), barColor);

                // Compare frame marker
                if (_compareReport != null && fdata.FrameIndex == _compareReport.FrameIndex)
                {
                    EditorGUI.DrawRect(new Rect(bx, _timelineRect.y, Mathf.Max(1f, barWidth), 3f),
                        OptifunityStyles.AccentPurple);
                }
            }

            // Mouse interaction
            var evt = Event.current;
            if (_timelineRect.Contains(evt.mousePosition))
            {
                float relX  = evt.mousePosition.x - _timelineRect.x;
                float fract = relX / _timelineRect.width;
                int   idx   = Mathf.Clamp((int)(fract * _timeline.Count), 0, _timeline.Count - 1);

                _hoveredFrameIndex = _timeline[idx].FrameIndex;

                // Tooltip
                string tip = $"Frame #{_timeline[idx].FrameIndex} — {_timeline[idx].TotalMs:F2}ms" +
                             (_timeline[idx].IsSpike ? " ⚠ SPIKE" : "");
                GUI.Label(new Rect(evt.mousePosition.x + 8, evt.mousePosition.y - 18, 200, 20),
                    tip, new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = Color.white, background = OptifunityStyles.TexHeader }
                    });

                if (evt.type == EventType.MouseDown && evt.button == 0)
                {
                    int clickedFrame = _timeline[idx].FrameIndex;

                    if (evt.shift)
                    {
                        // Shift+click → set compare frame
                        _compareFrameIndex = clickedFrame;
                        _compareReport     = SpikeAnalysisRunner.AnalyzeFrame(clickedFrame);
                        _analysisTab       = TAB_COMPARE; // Switch to Compare tab
                    }
                    else
                    {
                        // Normal click → analyze + pause editor like Profiler does
                        AnalyzeFrame(clickedFrame);
                        ProfilerHelper.SetSelectedFrame(clickedFrame);

                        // Pause playback so user can inspect the frame (same behavior as Profiler)
                        if (Application.isPlaying && !EditorApplication.isPaused)
                            EditorApplication.isPaused = true;
                    }
                    evt.Use();
                }

                Repaint();
            }
            else
            {
                _hoveredFrameIndex = -1;
            }

            // Legend
            GUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                DrawLegend("● Spike",    OptifunityStyles.ColorError);
                GUILayout.Space(6);
                DrawLegend("◆ > 16ms",  OptifunityStyles.ColorWarning);
                GUILayout.Space(6);
                DrawLegend("○ Normal",  OptifunityStyles.ColorSuccess);
                GUILayout.Space(6);
                DrawLegend("■ Selected", OptifunityStyles.AccentBlue);
                GUILayout.Space(6);
                DrawLegend("■ Compare", OptifunityStyles.AccentPurple);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Avg: {SpikeDetector.RollingAverage:F2}ms | " +
                                $"Threshold: {SpikeDetector.RollingAverage * _spikeThreshold:F2}ms | " +
                                $"{_timeline.Count} frames",
                    new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = OptifunityStyles.TextSecondary } });
                GUILayout.Space(8);
            }
        }

        private static void DrawRefLine(Rect chart, float ms, float maxMs, Color color, string label)
        {
            if (ms > maxMs) return;
            float y = chart.y + chart.height - (ms / maxMs) * chart.height;
            EditorGUI.DrawRect(new Rect(chart.x, y, chart.width, 1f), color);
            GUI.Label(new Rect(chart.x + 2, y - 14, 40, 14), label,
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } });
        }

        private static void DrawLegend(string text, Color color)
        {
            GUILayout.Label(text, new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = color } });
        }

        // ─── Analysis Panel ────────────────────────────────────────────────────
        private void DrawAnalysisPanel()
        {
            if (_currentReport == null)
            {
                GUILayout.Space(20);

                int selFrame = ProfilerHelper.GetSelectedFrame();
                string hint = selFrame >= 0
                    ? $"  Đang theo dõi Profiler — frame hiện tại: #{selFrame}\n  Click vào một frame bất kỳ trong Profiler để phân tích."
                    : "  Mở Unity Profiler (Window → Analysis → Profiler)\n  và click vào bất kỳ frame nào trong timeline.";

                GUILayout.Label(hint,
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                return;
            }

            // Frame summary bar
            DrawFrameSummaryBar(_currentReport);

            // Diagnosis summary (workload + dominant contributors + first investigation point)
            DrawFrameDiagnosis(_currentReport);

            // Breakdown bar
            DrawBreakdownBar(_currentReport);

            OptifunityStyles.DrawSeparator();

            // Analysis tabs
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                _analysisTab = GUILayout.Toolbar(_analysisTab, _analysisTabs, GUILayout.Height(22));
                GUILayout.Space(8);
            }

            _scrollReport = EditorGUILayout.BeginScrollView(_scrollReport);

            switch (_analysisTab)
            {
                case TAB_BOTTLENECK:
                    DrawBottleneckTab(_currentReport);
                    break;
                case TAB_ROOT_CAUSE:
                    DrawRootCauseTab(_currentReport);
                    break;
                case TAB_SAMPLES:
                    DrawSamplesTab(_currentReport.TopSlowSamples, "Slowest Samples (by CPU time)");
                    break;
                case TAB_GC_ALLOC:
                    DrawSamplesTab(_currentReport.TopGCAllocSamples, "GC Allocators (by bytes)");
                    break;
                case TAB_RENDERING:
                    DrawRenderingTab(_currentReport);
                    break;
                case TAB_COMPARE:
                    DrawCompareTab();
                    break;
            }

            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        private void DrawFrameSummaryBar(SpikeAnalysisReport r)
        {
            Color severityColor = r.Severity switch
            {
                SpikeSeverity.Critical => OptifunityStyles.ColorError,
                SpikeSeverity.High     => new Color(0.9f, 0.5f, 0.1f),
                SpikeSeverity.Medium   => OptifunityStyles.ColorWarning,
                _                      => OptifunityStyles.ColorSuccess
            };

            using (new EditorGUILayout.HorizontalScope(OptifunityStyles.StyleCard))
            {
                // Frame badge
                var frameStyle = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 15, normal = { textColor = OptifunityStyles.AccentBlue } };
                GUILayout.Label($"Frame #{r.FrameIndex}", frameStyle, GUILayout.Width(100));

                // Time
                GUILayout.Label($"{r.FrameTotalMs:F2}ms",
                    new GUIStyle(EditorStyles.boldLabel)
                    { fontSize = 14, normal = { textColor = severityColor } },
                    GUILayout.Width(70));

                // Spike badge
                if (r.IsSpike)
                    OptifunityStyles.DrawBadge($"⚠ SPIKE {r.Severity}", severityColor);
                else
                    OptifunityStyles.DrawBadge("✓ Normal", OptifunityStyles.ColorSuccess);

                GUILayout.Space(8);

                // Bottleneck type  
                OptifunityStyles.DrawBadge(r.PrimaryBottleneck.ToString(), OptifunityStyles.AccentPurple);

                GUILayout.FlexibleSpace();

                // Navigate to Profiler
                if (GUILayout.Button("Open in Profiler", GUILayout.Width(110), GUILayout.Height(20)))
                {
                    ProfilerHelper.SetSelectedFrame(r.FrameIndex);
                    // Try to open Profiler window
                    EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
                }

                // Shift+click hint
                GUILayout.Label(" | Shift+Click để set Compare", OptifunityStyles.StyleSubtitle,
                    GUILayout.Width(170));
            }
        }

        private void DrawFrameDiagnosis(SpikeAnalysisReport r)
        {
            var snapshot = SpikeAnalysisRunner.GetCachedSnapshot(r.FrameIndex);
            float avg = Mathf.Max(0.01f, r.AverageFrameMs > 0 ? r.AverageFrameMs : SpikeDetector.RollingAverage);
            float ratio = r.FrameTotalMs / avg;
            float gpuMs = snapshot != null && snapshot.IsValid ? snapshot.TotalGpuTimeMs : 0f;

            string workloadSummary = gpuMs > 0.1f
                ? (gpuMs > r.FrameTotalMs * 1.2f
                    ? "GPU-bound"
                    : r.FrameTotalMs > gpuMs * 1.2f
                        ? "CPU-bound"
                        : "Balanced")
                : "CPU-focused (no GPU time data)";

            var dominant = (r.TopSlowSamples ?? new List<ProfilerSample>())
                .Where(s => s.TotalTimeMs > 0.01f)
                .OrderByDescending(s => s.PercentOfFrame)
                .ThenByDescending(s => s.TotalTimeMs)
                .Take(3)
                .ToList();

            var first = dominant.FirstOrDefault();
            string firstPath = first != null ? GetCachedSamplePath(first.Name) : null;
            string firstTarget = first == null
                ? "Chưa đủ dữ liệu sample"
                : !string.IsNullOrEmpty(firstPath)
                    ? firstPath
                    : $"{first.Name} (Unity/engine marker hoặc không map được asset)";

            DrawCard(() =>
            {
                GUILayout.Label("FRAME DIAGNOSIS", new GUIStyle(OptifunityStyles.StyleIssueTitle)
                {
                    normal = { textColor = OptifunityStyles.AccentBlue },
                    fontSize = 12
                });

                GUILayout.Space(3);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawDiagnosisMetric("Workload", workloadSummary, 220);
                    DrawDiagnosisMetric("Spike Ratio", $"{ratio:F2}x avg", 120);
                    DrawDiagnosisMetric("Problem", r.IsSpike ? $"Yes ({r.Severity})" : "No", 120);
                    DrawDiagnosisMetric("Confidence", $"{r.PrimaryConfidence * 100:F0}%", 110);
                    GUILayout.FlexibleSpace();
                }

                GUILayout.Space(4);
                GUILayout.Label("Dominant Contributors (Top 3)", OptifunityStyles.StyleSubtitle);

                if (dominant.Count == 0)
                {
                    GUILayout.Label("  Không có sample đủ dữ liệu để xếp hạng.", OptifunityStyles.StyleIssueDesc);
                }
                else
                {
                    for (int i = 0; i < dominant.Count; i++)
                    {
                        var s = dominant[i];
                        string weight = s.PercentOfFrame >= 20f
                            ? "Major"
                            : s.PercentOfFrame >= 10f
                                ? "Significant"
                                : "Minor";

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label($" {i + 1}.", new GUIStyle(EditorStyles.boldLabel)
                            {
                                normal = { textColor = OptifunityStyles.AccentBlue },
                                fontSize = 10
                            }, GUILayout.Width(22));

                            GUILayout.Label($"{s.Name}", new GUIStyle(EditorStyles.label)
                            {
                                normal = { textColor = OptifunityStyles.TextPrimary },
                                fontSize = 10
                            }, GUILayout.Width(260));

                            GUILayout.Label($"{s.TotalTimeMs:F2}ms", new GUIStyle(EditorStyles.label)
                            {
                                normal = { textColor = OptifunityStyles.ColorWarning },
                                fontSize = 10
                            }, GUILayout.Width(62));

                            GUILayout.Label($"{s.PercentOfFrame:F1}%", new GUIStyle(EditorStyles.label)
                            {
                                normal = { textColor = OptifunityStyles.ColorInfo },
                                fontSize = 10
                            }, GUILayout.Width(55));

                            OptifunityStyles.DrawBadge(weight,
                                weight == "Major"
                                    ? OptifunityStyles.ColorError
                                    : weight == "Significant"
                                        ? OptifunityStyles.ColorWarning
                                        : OptifunityStyles.ColorInfo);

                            GUILayout.FlexibleSpace();
                        }
                    }
                }

                GUILayout.Space(4);
                GUILayout.Label("First place to inspect", OptifunityStyles.StyleSubtitle);
                GUILayout.Label($"  {firstTarget}", new GUIStyle(OptifunityStyles.StyleIssueDesc)
                {
                    wordWrap = true,
                    normal = { textColor = new Color(0.8f, 0.9f, 0.75f) }
                });
            }, new Color(0.1f, 0.16f, 0.20f));
        }

        private void DrawDiagnosisMetric(string label, string value, float width)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
            {
                GUILayout.Label(label, new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = OptifunityStyles.TextSecondary }
                });
                GUILayout.Label(value, new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = OptifunityStyles.TextPrimary },
                    fontSize = 11
                });
            }
        }

        private void DrawBreakdownBar(SpikeAnalysisReport r)
        {
            if (r.CategoryBreakdown == null || r.CategoryBreakdown.Count == 0) return;

            using (new EditorGUILayout.VerticalScope(OptifunityStyles.StyleCard))
            {
                var rect = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

                float currentX = rect.x;
                foreach (var b in r.CategoryBreakdown)
                {
                    float width = rect.width * b.Percentage;
                    var segmentRect = new Rect(currentX, rect.y, width, rect.height);
                    Color catColor = GetCategoryColor(b.Category);
                    EditorGUI.DrawRect(segmentRect, catColor);
                    
                    // Tooltip-like label if wide enough
                    if (width > 40)
                    {
                        var labelStyle = new GUIStyle(EditorStyles.miniLabel) 
                        { 
                            alignment = TextAnchor.MiddleCenter, 
                            normal = { textColor = Color.white },
                            fontSize = 9
                        };
                        GUI.Label(segmentRect, $"{b.Percentage * 100:F0}%", labelStyle);
                    }
                    
                    currentX += width;
                }

                GUILayout.Space(2);
                
                // Legend
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (var b in r.CategoryBreakdown.Take(5)) // Show top 5 in legend
                    {
                        var dotStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = GetCategoryColor(b.Category)} };
                        GUILayout.Label("●", dotStyle, GUILayout.Width(12));
                        GUILayout.Label($"{b.Category} ({b.Percentage * 100:F0}%)", EditorStyles.miniLabel);
                        GUILayout.Space(8);
                    }
                }
            }
        }

        private Color GetCategoryColor(ProfilerCategory cat) => cat switch
        {
            ProfilerCategory.Physics           => new Color(0.3f, 0.6f, 0.9f),
            ProfilerCategory.Rendering         => new Color(0.9f, 0.4f, 0.4f),
            ProfilerCategory.ScriptUpdate       => new Color(0.4f, 0.9f, 0.4f),
            ProfilerCategory.GarbageCollection => new Color(0.9f, 0.7f, 0.2f),
            ProfilerCategory.Animation         => new Color(0.6f, 0.4f, 0.9f),
            ProfilerCategory.UI                => new Color(0.2f, 0.8f, 0.8f),
            ProfilerCategory.Audio             => new Color(0.8f, 0.3f, 0.8f),
            ProfilerCategory.AssetLoading      => new Color(1.0f, 0.5f, 0.1f),
            ProfilerCategory.Overhead          => new Color(0.5f, 0.5f, 0.5f),
            _                                  => new Color(0.3f, 0.3f, 0.3f)
        };

        // ─── Bottleneck Tab ────────────────────────────────────────────────────
        private void DrawBottleneckTab(SpikeAnalysisReport r)
        {
            // Primary Bottleneck card
            Color primaryColor = GetBottleneckColor(r.PrimaryBottleneck);
            DrawCard(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("PRIMARY BOTTLENECK",
                        new GUIStyle(OptifunityStyles.StyleLabelError) { fontSize = 9 },
                        GUILayout.Width(140));
                    OptifunityStyles.DrawBadge(
                        $"Confidence: {r.PrimaryConfidence * 100:F0}%", primaryColor);
                }

                GUILayout.Label(r.PrimaryTitle,
                    new GUIStyle(OptifunityStyles.StyleIssueTitle)
                    { fontSize = 13, normal = { textColor = primaryColor } });

                GUILayout.Space(4);
                GUILayout.Label(r.PrimaryDescription, OptifunityStyles.StyleIssueDesc);

            }, primaryColor * 0.25f);

            // Key Metrics
            if (r.KeyMetrics.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("  ──  Key Metrics", OptifunityStyles.StyleHeader);
                DrawCard(() =>
                {
                    int col = 0;
                    EditorGUILayout.BeginHorizontal();
                    foreach (var m in r.KeyMetrics)
                    {
                        GUILayout.Label($"  {m}",
                            new GUIStyle(EditorStyles.label)
                            { normal = { textColor = OptifunityStyles.ColorInfo }, fontSize = 10 },
                            GUILayout.Width(position.width / 2 - 30));

                        if (++col % 2 == 0)
                        {
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.BeginHorizontal();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                });
            }

            // Contributing Factors
            if (r.ContributingFactors.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("  ──  Contributing Factors", OptifunityStyles.StyleHeader);

                foreach (var cf in r.ContributingFactors)
                {
                    Color cfColor = GetBottleneckColor(cf.Type);
                    DrawCard(() =>
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(cf.Type.ToString(),
                                new GUIStyle(EditorStyles.boldLabel)
                                { fontSize = 11, normal = { textColor = cfColor } },
                                GUILayout.Width(200));
                            OptifunityStyles.DrawBadge(
                                $"{cf.Confidence * 100:F0}% | {cf.TimeMs:F2}ms", cfColor);
                        }
                        GUILayout.Label(cf.Evidence, OptifunityStyles.StyleIssueDesc);
                    }, cfColor * 0.15f);
                }
            }

            // Recommendations
            GUILayout.Space(6);
            GUILayout.Label("  ──  Action Items", OptifunityStyles.StyleHeader);
            DrawCard(() =>
            {
                if (r.Recommendations.Count == 0)
                {
                    GUILayout.Label("  Không có khuyến nghị cụ thể.",
                        OptifunityStyles.StyleIssueDesc);
                    return;
                }

                for (int i = 0; i < r.Recommendations.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($" {i + 1}.",
                            new GUIStyle(EditorStyles.boldLabel)
                            { normal = { textColor = OptifunityStyles.AccentBlue }, fontSize = 10 },
                            GUILayout.Width(20));

                        bool isPrimary = r.Recommendations[i].StartsWith("[CHÍNH]");
                        Color rColor = isPrimary
                            ? new Color(0.7f, 0.95f, 0.7f)
                            : new Color(0.7f, 0.82f, 0.65f);

                        GUILayout.Label(r.Recommendations[i],
                            new GUIStyle(OptifunityStyles.StyleIssueDesc)
                            { normal = { textColor = rColor } });
                    }
                    if (i < r.Recommendations.Count - 1)
                        EditorGUI.DrawRect(
                            GUILayoutUtility.GetRect(0, 0.5f, GUILayout.ExpandWidth(true)),
                            new Color(0.3f, 0.35f, 0.4f, 0.3f));
                }
            });
        }

        // ─── Samples Tab ──────────────────────────────────────────────────────
        private void DrawSamplesTab(List<ProfilerSample> samples, string header)
        {
            GUILayout.Label($"  {header}", OptifunityStyles.StyleHeader);

            if (samples == null || samples.Count == 0)
            {
                GUILayout.Space(10);
                GUILayout.Label("  Không có dữ liệu sample.", OptifunityStyles.StyleIssueDesc);
                return;
            }

            // Column headers
            DrawCard(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Sample Name", OptifunityStyles.StyleIssueTitle, GUILayout.Width(280));
                    GUILayout.Label("Total", OptifunityStyles.StyleIssueTitle, GUILayout.Width(65));
                    GUILayout.Label("Self", OptifunityStyles.StyleIssueTitle, GUILayout.Width(60));
                    GUILayout.Label("GC Alloc", OptifunityStyles.StyleIssueTitle, GUILayout.Width(75));
                    GUILayout.Label("Calls", OptifunityStyles.StyleIssueTitle, GUILayout.Width(50));
                    GUILayout.Label("% Frame", OptifunityStyles.StyleIssueTitle, GUILayout.Width(60));
                    GUILayout.Label("Locate", OptifunityStyles.StyleIssueTitle, GUILayout.Width(55));
                }
            }, OptifunityStyles.BgHeader);

            float topTime = samples.Count > 0 ? samples[0].TotalTimeMs : 1f;

            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                bool isExpensive = s.TotalTimeMs > 5f;
                Color rowBg = i % 2 == 0
                    ? new Color(0.16f, 0.18f, 0.20f)
                    : new Color(0.13f, 0.15f, 0.17f);

                DrawCard(() =>
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // Profiling bar behind the name
                        Color msColor = s.TotalTimeMs > 10f
                            ? OptifunityStyles.ColorError
                            : s.TotalTimeMs > 5f
                            ? OptifunityStyles.ColorWarning
                            : OptifunityStyles.TextSecondary;

                        GUILayout.Label(
                            $"{(isExpensive ? "⚠ " : "  ")}{s.Name}",
                            new GUIStyle(EditorStyles.label)
                            {
                                normal = { textColor = isExpensive ? msColor : OptifunityStyles.TextPrimary },
                                fontSize = 10
                            },
                            GUILayout.Width(280));

                        GUILayout.Label($"{s.TotalTimeMs:F2}ms",
                            new GUIStyle(EditorStyles.label)
                            { normal = { textColor = msColor }, fontSize = 10 },
                            GUILayout.Width(65));

                        GUILayout.Label($"{s.SelfTimeMs:F2}ms",
                            OptifunityStyles.StyleIssueDesc, GUILayout.Width(60));

                        if (s.GCAllocBytes > 0)
                            GUILayout.Label(FormatBytes(s.GCAllocBytes),
                                new GUIStyle(EditorStyles.label)
                                { normal = { textColor = OptifunityStyles.ColorError }, fontSize = 10 },
                                GUILayout.Width(75));
                        else
                            GUILayout.Label("-", OptifunityStyles.StyleIssueDesc, GUILayout.Width(75));

                        GUILayout.Label($"{s.CallCount}",
                            OptifunityStyles.StyleIssueDesc, GUILayout.Width(50));

                        GUILayout.Label($"{s.PercentOfFrame:F1}%",
                            new GUIStyle(EditorStyles.label)
                            { normal = { textColor = OptifunityStyles.ColorInfo }, fontSize = 10 },
                            GUILayout.Width(60));

                        // Locate button
                        string path = GetCachedSamplePath(s.Name);
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(15)))
                            {
                                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                                if (script != null) EditorGUIUtility.PingObject(script);
                            }
                        }
                        else
                        {
                            GUILayout.Space(54);
                        }
                    }
                }, rowBg);
            }
        }

        // ─── Root Cause Tab ────────────────────────────────────────────────────
        private void DrawRootCauseTab(SpikeAnalysisReport r)
        {
            GUILayout.Label("  Root Cause Ranking — vấn đề nằm ở đâu?", OptifunityStyles.StyleHeader);

            var topByCost = (r.TopSlowSamples ?? new List<ProfilerSample>())
                .Where(s => s.TotalTimeMs > 0.01f)
                .OrderByDescending(s => s.PercentOfFrame)
                .ThenByDescending(s => s.TotalTimeMs)
                .Take(12)
                .ToList();

            if (topByCost.Count == 0)
            {
                GUILayout.Space(10);
                GUILayout.Label("  Không có dữ liệu root-cause cho frame này.", OptifunityStyles.StyleIssueDesc);
                return;
            }

            DrawCard(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Source", OptifunityStyles.StyleIssueTitle, GUILayout.Width(260));
                    GUILayout.Label("Category", OptifunityStyles.StyleIssueTitle, GUILayout.Width(82));
                    GUILayout.Label("Total", OptifunityStyles.StyleIssueTitle, GUILayout.Width(55));
                    GUILayout.Label("% Frame", OptifunityStyles.StyleIssueTitle, GUILayout.Width(58));
                    GUILayout.Label("GC", OptifunityStyles.StyleIssueTitle, GUILayout.Width(62));
                    GUILayout.Label("Where", OptifunityStyles.StyleIssueTitle, GUILayout.Width(210));
                    GUILayout.Label("Locate", OptifunityStyles.StyleIssueTitle, GUILayout.Width(50));
                }
            }, OptifunityStyles.BgHeader);

            for (int i = 0; i < topByCost.Count; i++)
            {
                var s = topByCost[i];
                string locatePath = GetCachedSamplePath(s.Name);
                string where = !string.IsNullOrEmpty(locatePath)
                    ? locatePath
                    : "Unity/engine marker";

                Color rowBg = i % 2 == 0
                    ? new Color(0.16f, 0.18f, 0.20f)
                    : new Color(0.13f, 0.15f, 0.17f);

                DrawCard(() =>
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string source = s.PercentOfFrame >= 20f ? $"⚠ {s.Name}" : s.Name;
                        GUILayout.Label(source, new GUIStyle(EditorStyles.label)
                        {
                            normal = { textColor = OptifunityStyles.TextPrimary },
                            fontSize = 10
                        }, GUILayout.Width(260));

                        GUILayout.Label(s.Category.ToString(), new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal = { textColor = GetCategoryColor(s.Category) }
                        }, GUILayout.Width(82));

                        GUILayout.Label($"{s.TotalTimeMs:F2}", new GUIStyle(EditorStyles.label)
                        {
                            normal = { textColor = s.TotalTimeMs > 5f ? OptifunityStyles.ColorWarning : OptifunityStyles.TextSecondary },
                            fontSize = 10
                        }, GUILayout.Width(55));

                        GUILayout.Label($"{s.PercentOfFrame:F1}%", new GUIStyle(EditorStyles.label)
                        {
                            normal = { textColor = OptifunityStyles.ColorInfo },
                            fontSize = 10
                        }, GUILayout.Width(58));

                        GUILayout.Label(s.GCAllocBytes > 0 ? FormatBytes(s.GCAllocBytes) : "-", new GUIStyle(EditorStyles.label)
                        {
                            normal = { textColor = s.GCAllocBytes > 0 ? OptifunityStyles.ColorError : OptifunityStyles.TextSecondary },
                            fontSize = 10
                        }, GUILayout.Width(62));

                        GUILayout.Label(where, new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal = { textColor = !string.IsNullOrEmpty(locatePath) ? new Color(0.8f, 0.9f, 0.75f) : OptifunityStyles.TextSecondary }
                        }, GUILayout.Width(210));

                        if (!string.IsNullOrEmpty(locatePath))
                        {
                            if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(46), GUILayout.Height(15)))
                            {
                                var script = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(locatePath);
                                if (script != null) EditorGUIUtility.PingObject(script);
                            }
                        }
                        else
                        {
                            GUILayout.Space(50);
                        }
                    }
                }, rowBg);
            }
        }

        // ─── Rendering Tab ─────────────────────────────────────────────────────
        private void DrawRenderingTab(SpikeAnalysisReport r)
        {
            GUILayout.Label("  Rendering Analysis — Frame #" + r.FrameIndex, OptifunityStyles.StyleHeader);

            var snapshot = Optifunity.Editor.Module5.SpikeAnalysisRunner.GetCachedSnapshot(r.FrameIndex);
            bool hasData = snapshot != null && snapshot.IsValid;

            // GPU vs CPU time summary
            DrawCard(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    void DrawTimeBlock(string label, float ms, float budget, Color accent)
                    {
                        Color col = ms > budget * 1.5f ? OptifunityStyles.ColorError
                                  : ms > budget         ? OptifunityStyles.ColorWarning
                                  : OptifunityStyles.ColorSuccess;
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(160)))
                        {
                            GUILayout.Label(label, new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = accent } });
                            GUILayout.Label($"{ms:F2}ms",
                                new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, normal = { textColor = col } });
                            GUILayout.Label($"Budget: {budget:F1}ms (60fps)",
                                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = OptifunityStyles.TextSecondary } });
                        }
                    }

                    float gpuMs = hasData ? snapshot.TotalGpuTimeMs : 0f;
                    float cpuMs = r.FrameTotalMs;
                    DrawTimeBlock("CPU Frame", cpuMs, 16.67f, OptifunityStyles.AccentBlue);
                    GUILayout.Space(20);
                    DrawTimeBlock("GPU Frame", gpuMs, 16.67f, new Color(0.9f, 0.4f, 0.9f));
                    GUILayout.FlexibleSpace();

                    // GPU vs CPU bound indicator
                    string bound = gpuMs > cpuMs * 1.2f ? "👋 GPU Bound"
                                 : cpuMs > gpuMs * 1.2f ? "💀 CPU Bound"
                                 : "⚖️ Balanced";
                    Color bColor = gpuMs > cpuMs * 1.2f ? new Color(0.9f, 0.4f, 0.9f)
                                 : cpuMs > gpuMs * 1.2f ? OptifunityStyles.ColorWarning
                                 : OptifunityStyles.ColorSuccess;
                    GUILayout.Label(bound, new GUIStyle(EditorStyles.boldLabel)
                        { normal = { textColor = bColor }, fontSize = 13 });
                }
            });

            GUILayout.Space(6);

            if (!hasData)
            {
                EditorGUILayout.HelpBox("Không có dữ liệu chi tiết cho frame này.\nHãy ghi lại trong Profiler và click vào frame cần phân tích.", MessageType.Info);
                return;
            }

            // ─── Rendering markers ───────────────────────
            GUILayout.Label("  Rendering Breakdown", OptifunityStyles.StyleHeader);

            // Marker data from hierarchy
            float cameraRenderMs   = snapshot.SumTime("Camera.Render");
            float cullingMs        = snapshot.SumTime("Camera.Cull");
            float shadowMs         = snapshot.SumTime("RenderShadowMaps") + snapshot.SumTime("ShadowMap Rendering");
            float urpRenderMs      = snapshot.SumTime("UniversalRenderPipeline.RenderSingleCamera");
            float opaqueMs         = snapshot.SumTime("RenderLoop.DrawSRPBatcher") + snapshot.SumTime("SRPBatcherSortObjects");
            float transparentMs    = snapshot.SumTime("Transparent") + snapshot.SumTime("TransparentGeometry");
            float postFxMs         = snapshot.SumTime("PostProcessing") + snapshot.SumTime("Stop NaN");
            float uiMs             = snapshot.SumTime("Canvas.Render") + snapshot.SumTime("Canvas.BuildBatch");
            float waitPresentMs    = snapshot.SumTime("Gfx.WaitForPresent");

            void DrawRenderRow(string label, float ms, float budget, string tip)
            {
                if (ms < 0.01f) return; // skip empty
                Color rowColor = ms > budget ? OptifunityStyles.ColorError
                               : ms > budget * 0.7f ? OptifunityStyles.ColorWarning
                               : OptifunityStyles.TextSecondary;
                DrawCard(() =>
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        float pct = r.FrameTotalMs > 0 ? ms / r.FrameTotalMs * 100f : 0f;
                        GUILayout.Label($"  {label}",
                            new GUIStyle(EditorStyles.label) { normal = { textColor = OptifunityStyles.TextPrimary }, fontSize = 10 },
                            GUILayout.Width(250));
                        GUILayout.Label($"{ms:F2}ms",
                            new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = rowColor }, fontSize = 10 },
                            GUILayout.Width(70));
                        GUILayout.Label($"{pct:F1}%",
                            new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = OptifunityStyles.ColorInfo } },
                            GUILayout.Width(50));
                        if (!string.IsNullOrEmpty(tip) && ms > budget)
                            GUILayout.Label($"⚠️ {tip}",
                                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = OptifunityStyles.ColorWarning }, wordWrap = true });
                    }
                }, ms > budget ? new Color(0.25f, 0.1f, 0.1f) : new Color(0.13f, 0.15f, 0.17f));
            }

            DrawRenderRow("Camera.Render (Total)",  cameraRenderMs, 12f, "Camera.Render quá cao — kiểm tra draw calls, culling");
            DrawRenderRow("URP RenderSingleCamera", urpRenderMs,   10f, "URP Pass quá nhiều — tắt bớt Renderer Features không dùng");
            DrawRenderRow("├ Camera Culling",       cullingMs,     2f,  "Culling chậm — giảm OccluderGeometry, dùng Layer mask hiệu quả");
            DrawRenderRow("├ Shadow Maps",          shadowMs,      3f,  "Shadow render đắt — giảm Shadow Cascades, Distance, Resolution");
            DrawRenderRow("├ Opaque Geometry",      opaqueMs,      5f,  "Opaque pass chậm — bật SRP Batcher, giảm SetPass");
            DrawRenderRow("├ Transparent",          transparentMs, 2f,  "Transparent pass tốn — sort đắt, giảm overdraw");
            DrawRenderRow("├ Post Processing",      postFxMs,      3f,  "Post FX đắt — tắt Bloom/DOF trên mobile hoặc giảm resolution");
            DrawRenderRow("UI Canvas Render",        uiMs,          2f,  "Canvas rebuild đắt — tách UI động vs tĩnh, dùng SetActive ít");
            DrawRenderRow("Gfx.WaitForPresent",      waitPresentMs, 4f,  "GPU Bound! GPU chưa xong khi CPU đợi — giảm shader complexity");

            // Rendering tips
            GUILayout.Space(6);
            DrawCard(() =>
            {
                GUILayout.Label("💡 Rendering Optimization Tips", OptifunityStyles.StyleIssueTitle);
                GUILayout.Space(3);
                GUILayout.Label("  1. Bật SRP Batcher (Project Settings → Graphics): giảm SetPass Calls ~60%", OptifunityStyles.StyleIssueDesc);
                GUILayout.Label("  2. GPU Instancing cho mesh giống nhau (≥ 100 instance)", OptifunityStyles.StyleIssueDesc);
                GUILayout.Label("  3. Shadow Distance nhỏ lại (vd: 50m thay vì 150m) giảm nặng shadow pass", OptifunityStyles.StyleIssueDesc);
                GUILayout.Label("  4. Occlusion Culling cho scene tĩnh — giảm render object bị che khuất", OptifunityStyles.StyleIssueDesc);
                GUILayout.Label("  5. DrawMeshInstanced API cho vật thể procedural (≥ 1000 objects)", OptifunityStyles.StyleIssueDesc);
            }, new Color(0.1f, 0.15f, 0.1f));
        }

        // ─── Compare Tab ──────────────────────────────────────────────────────
        private void DrawCompareTab()
        {
            GUILayout.Label("  Frame Comparison (Shift+Click timeline để chọn frame so sánh)",
                OptifunityStyles.StyleHeader);

            if (_compareReport == null)
            {
                GUILayout.Space(16);
                GUILayout.Label(
                    "  Chưa chọn frame để so sánh.\n  Shift+Click vào bar trong Timeline để đặt Compare frame.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                return;
            }

            // So sánh hai report cạnh nhau
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCompareColumn("Current Frame", _currentReport, OptifunityStyles.AccentBlue);
                GUILayout.Space(4);
                DrawCompareColumn("Compare Frame", _compareReport, OptifunityStyles.AccentPurple);
            }
        }

        private void DrawCompareColumn(string label, SpikeAnalysisReport r, Color accentColor)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width / 2 - 12)))
            {
                // Header
                GUILayout.Label(label,
                    new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = accentColor } });

                DrawCard(() =>
                {
                    var fs = r.FrameTotalMs;
                    Color c = fs > 33.33f ? OptifunityStyles.ColorError :
                              fs > 16.67f ? OptifunityStyles.ColorWarning :
                              OptifunityStyles.ColorSuccess;

                    GUILayout.Label($"Frame #{r.FrameIndex}",
                        new GUIStyle(EditorStyles.label)
                        { normal = { textColor = accentColor }, fontSize = 11 });

                    GUILayout.Label($"{r.FrameTotalMs:F2}ms",
                        new GUIStyle(EditorStyles.boldLabel)
                        { normal = { textColor = c }, fontSize = 16 });

                    OptifunityStyles.DrawBadge(r.PrimaryBottleneck.ToString(), accentColor);

                    GUILayout.Space(4);
                    foreach (var m in r.KeyMetrics)
                        GUILayout.Label($"  {m}", OptifunityStyles.StyleIssueDesc);

                }, accentColor * 0.15f);
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private void DrawCard(Action content, Color? bgColor = null)
        {
            Color color = bgColor ?? OptifunityStyles.BgCard;
            int key = ColorToKey(color);

            if (!_cardTextureCache.TryGetValue(key, out var texture) || texture == null)
            {
                texture = OptifunityStyles.MakeTexture(color);
                _cardTextureCache[key] = texture;
            }

            var style = new GUIStyle(OptifunityStyles.StyleCard)
            {
                normal = { background = texture },
                margin = new RectOffset(8, 8, 2, 2)
            };
            using (new EditorGUILayout.VerticalScope(style))
                content();
        }

        private static Color GetBottleneckColor(BottleneckType type) => type switch
        {
            BottleneckType.GarbageCollection => OptifunityStyles.ColorError,
            BottleneckType.GPUBound          => new Color(0.9f, 0.4f, 0.9f),
            BottleneckType.CPURenderThread   => new Color(0.9f, 0.55f, 0.15f),
            BottleneckType.Physics           => new Color(0.3f, 0.7f, 1.0f),
            BottleneckType.Scripting         => OptifunityStyles.ColorWarning,
            BottleneckType.UICanvas          => new Color(0.8f, 0.6f, 0.1f),
            BottleneckType.Animation         => new Color(0.4f, 0.8f, 0.6f),
            BottleneckType.AssetLoading      => new Color(1.0f, 0.3f, 0.3f),
            BottleneckType.Audio             => new Color(0.5f, 0.8f, 0.5f),
            BottleneckType.VSync             => OptifunityStyles.ColorInfo,
            BottleneckType.JobSystemStall    => new Color(0.7f, 0.5f, 1.0f),
            BottleneckType.Mixed             => OptifunityStyles.ColorWarning,
            BottleneckType.Balanced          => OptifunityStyles.ColorSuccess,
            _                               => OptifunityStyles.TextSecondary
        };

        private static int ColorToKey(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            return (r << 24) | (g << 16) | (b << 8) | a;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1048576) return $"{bytes / 1048576f:F1}MB";
            if (bytes >= 1024)    return $"{bytes / 1024f:F1}KB";
            return $"{bytes}B";
        }
    }
}
