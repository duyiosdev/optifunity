using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Optifunity.Editor.UI;

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
        private Vector2 _scrollSamples;

        private bool _autoFollowProfiler = true;
        private bool _showComparision;
        private bool _showAllSamples;
        private bool _showRawGCAlloc = true;
        private float _spikeThreshold = SpikeDetector.DEFAULT_SPIKE_FACTOR;

        // Timeline chart state
        private Rect  _timelineRect;
        private int   _hoveredFrameIndex = -1;

        // Tabs trong Analysis panel
        private int _analysisTab;
        private readonly string[] _analysisTabs = { "Bottleneck", "Samples", "GC Alloc", "Compare" };

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
            _timeline = SpikeDetector.FrameBuffer;

            // Phân tích frame hiện tại nếu có
            int curFrame = ProfilerDriver.selectedFrame;
            if (curFrame >= 0) AnalyzeFrame(curFrame);
        }

        private void OnDisable()
        {
            SpikeDetector.OnFrameSelected   -= OnProfilerFrameSelected;
            SpikeDetector.OnTimelineUpdated -= OnTimelineUpdated;
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

        // Gọi 10 lần/giây — đủ để phát hiện frame selection change
        private void OnInspectorUpdate()
        {
            if (!_autoFollowProfiler) return;
            int cur = ProfilerDriver.selectedFrame;
            if (cur >= 0 && (_currentReport == null || cur != _currentReport.FrameIndex))
                Repaint();
        }

        private void AnalyzeFrame(int frameIndex)
        {
            _currentReport = SpikeAnalysisRunner.AnalyzeFrame(frameIndex);
            Repaint();
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
                GUI.Label(_timelineRect, "  Chưa có dữ liệu Profiler. Mở Profiler window và bắt đầu record.",
                    new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = OptifunityStyles.TextSecondary },
                        alignment = TextAnchor.MiddleCenter
                    });
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
                        _analysisTab       = 3; // Switch to Compare tab
                    }
                    else
                    {
                        // Normal click → analyze
                        AnalyzeFrame(clickedFrame);
                        // Sync Profiler selection
                        ProfilerDriver.selectedFrame = clickedFrame;
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
                GUILayout.Label("  Click vào một frame trong Timeline hoặc Profiler window để phân tích.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 });
                return;
            }

            // Frame summary bar
            DrawFrameSummaryBar(_currentReport);
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
                case 0: DrawBottleneckTab(_currentReport); break;
                case 1: DrawSamplesTab(_currentReport.TopSlowSamples, "Slowest Samples (by CPU time)"); break;
                case 2: DrawSamplesTab(_currentReport.TopGCAllocSamples, "GC Allocators (by bytes)"); break;
                case 3: DrawCompareTab(); break;
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
                    ProfilerDriver.selectedFrame = r.FrameIndex;
                    // Try to open Profiler window
                    EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
                }

                // Shift+click hint
                GUILayout.Label(" | Shift+Click để set Compare", OptifunityStyles.StyleSubtitle,
                    GUILayout.Width(170));
            }
        }

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
                    }
                }, rowBg);
            }
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

        private static void DrawCard(Action content, Color? bgColor = null)
        {
            var style = new GUIStyle(OptifunityStyles.StyleCard)
            {
                normal = { background = OptifunityStyles.MakeTexture(bgColor ?? OptifunityStyles.BgCard) },
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

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1048576) return $"{bytes / 1048576f:F1}MB";
            if (bytes >= 1024)    return $"{bytes / 1024f:F1}KB";
            return $"{bytes}B";
        }
    }
}
