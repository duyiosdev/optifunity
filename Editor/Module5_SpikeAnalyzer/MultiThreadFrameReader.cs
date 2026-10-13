using System;
using System.Collections.Generic;
using UnityEditorInternal;
using UnityEngine;

namespace Optifunity.Editor.Module5
{
    [Serializable] public class ThreadEvidence
    {
        public int index;
        public string name, group;
        public int sampleCount;
        public bool truncated;
        public ulong id;
        public float frameMs;
        public List<ThreadMarker> markers = new();
    }
    [Serializable] public class ThreadMarker { public string name; public float inclusiveMs; }
    [Serializable] public class MultiThreadEvidence
    {
        public int frameIndex, profilerTarget;
        public string capturedAt, status, scope = "Per-thread inclusive marker times; concurrent thread times must not be added to derive frame wall time";
        public List<ThreadEvidence> threads = new();
    }
    public static class MultiThreadFrameReader
    {
        public static MultiThreadEvidence Read(int frameIndex)
        {
            var result = new MultiThreadEvidence { frameIndex = frameIndex, profilerTarget = ProfilerDriver.connectedProfiler,
                capturedAt = DateTime.UtcNow.ToString("o"), status = "Unknown: no valid frame" };
            if (frameIndex < ProfilerDriver.firstFrameIndex || frameIndex > ProfilerDriver.lastFrameIndex || frameIndex < 0) return result;
            try
            {
                for (int i = 0; i < 256; i++)
                {
                    using var view = ProfilerDriver.GetRawFrameDataView(frameIndex, i);
                    if (!view.valid) { result.status = result.threads.Count > 0 ? (result.threads.Exists(t => t.truncated) ? "Incomplete: sample limit reached" : "Complete within captured threads; top 20 markers each") : result.status; return result; }
                    var thread = new ThreadEvidence { index = i, name = view.threadName, group = view.threadGroupName, id = view.threadId, frameMs = view.frameTimeMs, sampleCount = view.sampleCount, truncated = view.sampleCount > 200000 };
                    // Keep the largest raw samples; they overlap and are not a summed cost attribution.
                    for (int j = 0; j < Math.Min(view.sampleCount, 200000); j++)
                    {
                        float ms = view.GetSampleTimeMs(j);
                        if (thread.markers.Count == 20 && ms <= thread.markers[19].inclusiveMs) continue;
                        thread.markers.Add(new ThreadMarker { name = view.GetSampleName(j), inclusiveMs = ms });
                        thread.markers.Sort((a,b) => b.inclusiveMs.CompareTo(a.inclusiveMs));
                        if (thread.markers.Count > 20) thread.markers.RemoveAt(20);
                    }
                    result.threads.Add(thread);
                }
                result.status = "Incomplete: thread limit 256 reached";
            }
            catch (Exception ex) { result.status = "Incomplete/unsupported: " + ex.Message; }
            return result;
        }
    }
}
