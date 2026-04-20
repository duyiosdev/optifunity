using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;

namespace Optifunity.Editor.Module5
{
    /// <summary>
    /// Monitor ProfilerDriver.selectedFrame và xây dựng rolling buffer của frame times.
    /// Phát event khi frame được chọn thay đổi.
    /// </summary>
    [InitializeOnLoad]
    public static class SpikeDetector
    {
        // ─── Config ────────────────────────────────────────────────────────────
        public  const int   BUFFER_CAPACITY       = 300;  // Giữ 300 frames
        public  const float DEFAULT_SPIKE_FACTOR  = 1.5f; // 1.5x avg = spike
        private const float POLL_INTERVAL_SECS    = 0.1f; // poll 10x/s

        // ─── State ────────────────────────────────────────────────────────────
        private static int       _lastSelectedFrame = -1;
        private static double    _lastPollTime;
        private static bool      _isEnabled;

        // Rolling frame time buffer
        private static readonly Queue<FrameTimeData> _frameBuffer = new();
        private static float   _rollingAverage = 16.67f;
        public  static float   SpikeFactor     = DEFAULT_SPIKE_FACTOR;

        // ─── Events ────────────────────────────────────────────────────────────
        /// <summary>Fired khi một frame được chọn (bất kỳ).</summary>
        public static event Action<int, bool> OnFrameSelected;  // (frameIndex, isSpike)

        /// <summary>Fired khi rolling buffer được cập nhật (timeline refresh).</summary>
        public static event Action<IReadOnlyList<FrameTimeData>> OnTimelineUpdated;

        // ─── Initialization ────────────────────────────────────────────────────
        static SpikeDetector()
        {
            // InitializeOnLoad → đăng ký ngay khi Domain reload xong
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
                if (value)
                    EditorApplication.update += Poll;
                else
                    EditorApplication.update -= Poll;
            }
        }

        // ─── Rolling Data ──────────────────────────────────────────────────────
        public static float RollingAverage => _rollingAverage;

        public static IReadOnlyList<FrameTimeData> FrameBuffer
        {
            get
            {
                lock (_frameBuffer)
                    return new List<FrameTimeData>(_frameBuffer);
            }
        }

        // ─── Poll ──────────────────────────────────────────────────────────────
        private static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastPollTime < POLL_INTERVAL_SECS) return;
            _lastPollTime = now;

            // ─── Kiểm tra xem Profiler có data không ──────────────────────────
            int first = ProfilerDriver.firstFrameIndex;
            int last  = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < 0) return;

            // ─── Cập nhật rolling buffer nếu có frame mới ─────────────────────
            RefreshTimeline(first, last);

            // ─── Kiểm tra frame selection thay đổi ────────────────────────────
            int selected = ProfilerDriver.selectedFrame;
            if (selected == _lastSelectedFrame || selected < 0) return;

            _lastSelectedFrame = selected;
            bool isSpike = DetectSpike(selected);
            OnFrameSelected?.Invoke(selected, isSpike);
        }

        private static void RefreshTimeline(int first, int last)
        {
            // Chỉ đọc frame mới nhất để tránh đọc lại toàn bộ buffer mỗi poll
            lock (_frameBuffer)
            {
                int bufferLastFrame = _frameBuffer.Count > 0
                    ? _frameBuffer.ToArray()[^1].FrameIndex
                    : first - 1;

                // Nếu buffer đã cũ hoặc profiler reset → clear và rebuild
                if (_frameBuffer.Count > 0 &&
                    _frameBuffer.ToArray()[0].FrameIndex > first + 10)
                {
                    _frameBuffer.Clear();
                    bufferLastFrame = first - 1;
                }

                // Đọc các frames mới
                int startRead = Math.Max(first, bufferLastFrame + 1);
                int endRead   = last;

                if (startRead > endRead) return;

                // Giới hạn số frames đọc mỗi poll để tránh lag
                int maxRead = Math.Min(20, endRead - startRead + 1);
                startRead   = endRead - maxRead + 1;

                var newFrames = FrameDataReader.ReadFrameTimeline(startRead, endRead);
                foreach (var f in newFrames)
                {
                    _frameBuffer.Enqueue(f);
                    while (_frameBuffer.Count > BUFFER_CAPACITY)
                        _frameBuffer.Dequeue();
                }

                // Tính lại rolling average
                if (_frameBuffer.Count > 0)
                {
                    float sum = 0; int cnt = 0;
                    foreach (var f in _frameBuffer)
                        if (f.TotalMs > 0.1f) { sum += f.TotalMs; cnt++; }
                    if (cnt > 0) _rollingAverage = sum / cnt;
                }
            }

            OnTimelineUpdated?.Invoke(FrameBuffer);
        }

        private static bool DetectSpike(int frameIndex)
        {
            // Tìm frame trong buffer
            lock (_frameBuffer)
            {
                foreach (var f in _frameBuffer)
                    if (f.FrameIndex == frameIndex)
                        return f.TotalMs > _rollingAverage * SpikeFactor;
            }

            // Nếu không có trong buffer, đọc từ profiler
            var newFrames = FrameDataReader.ReadFrameTimeline(frameIndex, frameIndex);
            if (newFrames.Length > 0)
                return newFrames[0].TotalMs > _rollingAverage * SpikeFactor;

            return false;
        }

        /// <summary>Reset toàn bộ buffer (ví dụ khi Profiler Clear)</summary>
        public static void Reset()
        {
            lock (_frameBuffer) _frameBuffer.Clear();
            _lastSelectedFrame = -1;
            _rollingAverage    = 16.67f;
        }

        /// <summary>Lấy FrameTimeData của frame cụ thể từ buffer</summary>
        public static bool TryGetFrameData(int frameIndex, out FrameTimeData data)
        {
            lock (_frameBuffer)
            {
                foreach (var f in _frameBuffer)
                {
                    if (f.FrameIndex == frameIndex)
                    {
                        data = f;
                        return true;
                    }
                }
            }
            data = default;
            return false;
        }

        public static int LastSelectedFrame => _lastSelectedFrame;
    }
}
