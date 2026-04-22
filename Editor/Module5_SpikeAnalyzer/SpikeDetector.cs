using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;

namespace Optifunity.Editor.Module5
{
    /// <summary>
    /// Poll ProfilerDriver frame data and fire events when the selected frame changes.
    /// Uses ProfilerHelper for the internal selectedFrame API.
    /// </summary>
    [InitializeOnLoad]
    public static class SpikeDetector
    {
        // ─── Config ────────────────────────────────────────────────────────────
        public  const int   BUFFER_CAPACITY      = 300;
        public  const float DEFAULT_SPIKE_FACTOR = 1.5f;
        private const float POLL_INTERVAL_SECS   = 0.08f; // ~12x/s

        // ─── State ─────────────────────────────────────────────────────────────
        private static int    _lastSelectedFrame = -1;
        private static int    _lastKnownLastFrame = -1;  // detect new profiler data
        private static double _lastPollTime;
        private static bool   _isEnabled;
        private static bool   _initialLoadDone;

        private static readonly Queue<FrameTimeData> _frameBuffer = new();
        private static float _rollingAverage = 16.67f;
        public  static float SpikeFactor = DEFAULT_SPIKE_FACTOR;

        // ─── Events ────────────────────────────────────────────────────────────
        public static event Action<int, bool>                        OnFrameSelected;
        public static event Action<IReadOnlyList<FrameTimeData>>     OnTimelineUpdated;

        // ─── Init ──────────────────────────────────────────────────────────────
        static SpikeDetector()
        {
            EditorApplication.update += Poll;
            _isEnabled = true;
        }

        public static bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (value == _isEnabled) return;
                _isEnabled = value;
                if (value) EditorApplication.update += Poll;
                else       EditorApplication.update -= Poll;
            }
        }

        public static float RollingAverage => _rollingAverage;

        public static IReadOnlyList<FrameTimeData> FrameBuffer
        {
            get { lock (_frameBuffer) return new List<FrameTimeData>(_frameBuffer); }
        }

        // ─── Poll ──────────────────────────────────────────────────────────────
        private static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastPollTime < POLL_INTERVAL_SECS) return;
            _lastPollTime = now;

            int first = ProfilerDriver.firstFrameIndex;
            int last  = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < 0) return;

            // ── On first valid data or after Profiler reset: bulk-load all frames ──
            bool profilerReset = last < _lastKnownLastFrame - 5;
            if (!_initialLoadDone || profilerReset)
            {
                _initialLoadDone    = true;
                _lastKnownLastFrame = last;
                BulkLoadTimeline(first, last);
            }
            else if (last > _lastKnownLastFrame)
            {
                // Incremental: only read new frames since last poll
                int startRead = _lastKnownLastFrame + 1;
                _lastKnownLastFrame = last;
                AppendToTimeline(startRead, last);
            }

            // ── Frame selection detection ──
            // Primary: use ProfilerHelper (handles both old/new Unity APIs)
            int selected = ProfilerHelper.GetSelectedFrame();

            // Fallback: if ProfilerHelper returns -1 (Profiler window not open),
            // we can't auto-detect selection — don't fire spurious events
            if (selected < 0) return;
            if (selected == _lastSelectedFrame) return;

            _lastSelectedFrame = selected;
            bool isSpike = IsFrameSpike(selected);
            OnFrameSelected?.Invoke(selected, isSpike);
        }

        // ─── Timeline Management ───────────────────────────────────────────────

        /// <summary>Load ALL available frames at once (up to BUFFER_CAPACITY)</summary>
        public static void BulkLoadTimeline(int first, int last)
        {
            lock (_frameBuffer)
            {
                _frameBuffer.Clear();

                // Clamp to buffer capacity from the END (most recent frames)
                int count      = last - first + 1;
                int startFrame = count > BUFFER_CAPACITY ? last - BUFFER_CAPACITY + 1 : first;

                var frames = FrameDataReader.ReadFrameTimeline(startFrame, last);
                RecalcAverage(frames);

                foreach (var f in frames)
                    _frameBuffer.Enqueue(f);
            }
            OnTimelineUpdated?.Invoke(FrameBuffer);
        }

        private static void AppendToTimeline(int startRead, int endRead)
        {
            // Cap batch to avoid hitching on large gaps
            int maxBatch = 50;
            if (endRead - startRead + 1 > maxBatch)
                startRead = endRead - maxBatch + 1;

            var newFrames = FrameDataReader.ReadFrameTimeline(startRead, endRead);
            lock (_frameBuffer)
            {
                foreach (var f in newFrames)
                {
                    _frameBuffer.Enqueue(f);
                    while (_frameBuffer.Count > BUFFER_CAPACITY)
                        _frameBuffer.Dequeue();
                }
                RecalcAverageFromBuffer();
            }
            OnTimelineUpdated?.Invoke(FrameBuffer);
        }

        private static void RecalcAverage(FrameTimeData[] frames)
        {
            float sum = 0; int cnt = 0;
            foreach (var f in frames)
                if (f.TotalMs > 0.1f) { sum += f.TotalMs; cnt++; }
            if (cnt > 0) _rollingAverage = sum / cnt;
        }

        private static void RecalcAverageFromBuffer()
        {
            float sum = 0; int cnt = 0;
            foreach (var f in _frameBuffer)
                if (f.TotalMs > 0.1f) { sum += f.TotalMs; cnt++; }
            if (cnt > 0) _rollingAverage = sum / cnt;
        }

        private static bool IsFrameSpike(int frameIndex)
        {
            lock (_frameBuffer)
            {
                foreach (var f in _frameBuffer)
                    if (f.FrameIndex == frameIndex)
                        return f.TotalMs > _rollingAverage * SpikeFactor;
            }
            // Not in buffer yet — read it
            var frames = FrameDataReader.ReadFrameTimeline(frameIndex, frameIndex);
            return frames.Length > 0 && frames[0].TotalMs > _rollingAverage * SpikeFactor;
        }

        public static void Reset()
        {
            lock (_frameBuffer) _frameBuffer.Clear();
            _lastSelectedFrame  = -1;
            _lastKnownLastFrame = -1;
            _initialLoadDone    = false;
            _rollingAverage     = 16.67f;
        }

        public static bool TryGetFrameData(int frameIndex, out FrameTimeData data)
        {
            lock (_frameBuffer)
            {
                foreach (var f in _frameBuffer)
                    if (f.FrameIndex == frameIndex) { data = f; return true; }
            }
            data = default;
            return false;
        }

        public static int LastSelectedFrame => _lastSelectedFrame;
    }
}
