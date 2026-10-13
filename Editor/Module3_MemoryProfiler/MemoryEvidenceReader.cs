using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling.Memory;

namespace Optifunity.Editor.Module3
{
    [Serializable] public class MemoryTypeTotal { public string type; public long bytes; public int count; }
    [Serializable] public class MemoryEvidence
    {
        public string capturedAt, projectPath, unityVersion, scope = "Editor loaded Unity objects; excludes managed heap graph, process RSS and device memory";
        public long unityAllocatedBytes;
        public List<MemoryTypeTotal> types = new();
    }
    [Serializable] public class MemoryDelta { public string type; public long byteDelta; public int countDelta; }
    [Serializable] public class MemoryComparison
    {
        public string scope = "Editor object type aggregates only; growth is not proof of a leak. Native .snap graph comparison requires Unity Memory Profiler.";
        public List<MemoryDelta> types = new();
    }
    public static class MemoryEvidenceReader
    {
        public static MemoryEvidence Read()
        {
            var result = new MemoryEvidence { capturedAt = DateTime.UtcNow.ToString("o"), projectPath = System.IO.Path.GetFullPath("."),
                unityVersion = Application.unityVersion, unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong() };
            var totals = new Dictionary<string, MemoryTypeTotal>();
            foreach (var obj in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (obj == null) continue;
                string type = obj.GetType().FullName;
                if (!totals.TryGetValue(type, out var total)) totals[type] = total = new MemoryTypeTotal { type = type };
                total.count++; total.bytes += Profiler.GetRuntimeMemorySizeLong(obj);
            }
            result.types = totals.Values.OrderByDescending(t => t.bytes).ToList();
            return result;
        }
        public static MemoryComparison Compare(MemoryEvidence before, MemoryEvidence after)
        {
            if (before == null || after == null || before.projectPath != after.projectPath || before.unityVersion != after.unityVersion || before.scope != after.scope)
                throw new ArgumentException("Memory snapshot contexts differ.");
            var old = before.types.ToDictionary(t => t.type); var current = after.types.ToDictionary(t => t.type);
            var result = new MemoryComparison();
            foreach (var type in old.Keys.Union(current.Keys).OrderBy(t => t))
            {
                old.TryGetValue(type, out var a); current.TryGetValue(type, out var b);
                result.types.Add(new MemoryDelta { type = type, byteDelta = (b?.bytes ?? 0) - (a?.bytes ?? 0), countDelta = (b?.count ?? 0) - (a?.count ?? 0) });
            }
            return result;
        }
        public static bool SnapshotPending { get; private set; }
        public static void CaptureNative(string path, Action<string, bool> complete)
        {
            if (SnapshotPending) throw new InvalidOperationException("A memory capture is already pending.");
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".snap", StringComparison.OrdinalIgnoreCase) || System.IO.File.Exists(path))
                throw new ArgumentException("Choose a new .snap path; existing captures are preserved.");
            SnapshotPending = true;
            try
            {
                Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(path, (saved, success) => { SnapshotPending = false; complete?.Invoke(saved, success); },
                    CaptureFlags.NativeObjects | CaptureFlags.ManagedObjects);
            }
            catch { SnapshotPending = false; throw; }
        }
    }
}
