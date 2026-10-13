using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace Optifunity.Runtime
{
    [Serializable] public class DeviceSample
    {
        public double elapsedSeconds;
        public float frameMs;
        public long unityAllocatedBytes;
        public long residentBytes = -1;
        public float thermalCelsius = -1;
        public string externalMetricSource;
    }
    [Serializable] public class LoadingMeasurement { public string name; public double milliseconds; }
    [Serializable] public class DeviceCapture
    {
        public int schemaVersion = 1;
        public string sessionId, scenario, variant, device, operatingSystem, unityVersion, project, buildId, graphicsApi, quality, capturedAt;
        public bool isEditor;
        public int targetFps;
        public float warmupSeconds;
        public long memoryBudgetBytes;
        public string memoryScope = "Unity allocated memory is not process RSS; unavailable resident/thermal metrics use -1";
        public List<DeviceSample> samples = new();
        public List<LoadingMeasurement> loading = new();
    }
    // Opt-in only: add explicitly to a measurement scene or create through your test harness.
    public sealed class DeviceEvidenceRecorder : MonoBehaviour
    {
        public string scenario = "", variant = "before";
        public int targetFps = 30, maxSamples = 18000;
        public long memoryBudgetBytes;
        public float warmupSeconds = 10;
        public DeviceCapture Capture { get; private set; }
        public bool IsRecording { get; private set; }
        private double start, last;
        public void Begin()
        {
            if (string.IsNullOrWhiteSpace(scenario) || targetFps <= 0 || maxSamples < 1 || warmupSeconds < 0)
                throw new InvalidOperationException("Set scenario, target FPS, sample limit and nonnegative warmup before recording.");
            Capture = new DeviceCapture { sessionId = Guid.NewGuid().ToString("N"), scenario = scenario, variant = variant,
                targetFps = targetFps, warmupSeconds = warmupSeconds, memoryBudgetBytes = memoryBudgetBytes, device = SystemInfo.deviceModel,
                operatingSystem = SystemInfo.operatingSystem, unityVersion = Application.unityVersion, project = string.IsNullOrEmpty(Application.identifier) ? Application.companyName + "/" + Application.productName : Application.identifier,
                buildId = Application.buildGUID, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()], capturedAt = DateTime.UtcNow.ToString("o"), isEditor = Application.isEditor };
            start = last = Time.realtimeSinceStartupAsDouble;
            IsRecording = true;
        }
        private void LateUpdate()
        {
            if (!IsRecording) return;
            double now = Time.realtimeSinceStartupAsDouble;
            float delta = (float)((now - last) * 1000); last = now;
            if (now - start < warmupSeconds) return;
            Capture.samples.Add(new DeviceSample { elapsedSeconds = now - start, frameMs = delta, unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong() });
            if (Capture.samples.Count >= maxSamples) IsRecording = false;
        }
        public void AddExternalMetrics(long residentBytes, float thermalCelsius, string source)
        {
            if (Capture == null || Capture.samples.Count == 0 || string.IsNullOrWhiteSpace(source))
                throw new InvalidOperationException("An existing sample and a measurement source are required.");
            if (residentBytes < -1 || float.IsNaN(thermalCelsius) || float.IsInfinity(thermalCelsius) || thermalCelsius < -1 || (residentBytes == -1 && thermalCelsius == -1))
                throw new ArgumentOutOfRangeException(nameof(residentBytes));
            var sample = Capture.samples[Capture.samples.Count - 1];
            sample.residentBytes = residentBytes; sample.thermalCelsius = thermalCelsius; sample.externalMetricSource = source;
        }
        public void AddLoadingMeasurement(string name, double milliseconds)
        {
            if (Capture == null || string.IsNullOrWhiteSpace(name) || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0)
                throw new ArgumentException("A capture, named loading interval and finite duration are required.");
            Capture.loading.Add(new LoadingMeasurement { name = name, milliseconds = milliseconds });
        }
        public string StopAndSave()
        {
            IsRecording = false;
            if (Capture == null) throw new InvalidOperationException("No capture started.");
            string path = Path.Combine(Application.persistentDataPath, "Optifunity-" + Capture.sessionId + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(Capture, true)); return path;
        }
    }
    [Serializable] public class CaptureSummary
    {
        public int frames, hitches, residentSamples, thermalSamples;
        public double durationSeconds, hitchRate;
        public double p50Ms, p95Ms, p99Ms, maxMs;
        public long peakUnityAllocatedBytes, peakResidentBytes = -1;
        public float peakThermalCelsius = -1;
        public string scope, memoryBudgetStatus;
    }
    [Serializable] public class CaptureComparison
    {
        public string status, scope;
        public CaptureSummary before, after;
        public double p95DeltaMs;
        public List<LoadingDelta> loading = new();
        public List<string> limitations = new();
    }
    [Serializable] public class LoadingDelta { public string name; public double beforeMs, afterMs, deltaMs; }
    public static class DeviceCaptureAnalysis
    {
        public static CaptureSummary Summarize(DeviceCapture capture)
        {
            if (capture == null || capture.schemaVersion != 1 || capture.targetFps <= 0 || capture.samples == null || capture.samples.Count == 0)
                throw new ArgumentException("Supported capture with samples and target FPS required.");
            var frames = new List<double>();
            var result = new CaptureSummary { frames = capture.samples.Count, scope = capture.isEditor ? "Editor measurement; not device benchmark" : "Frame intervals including waits; not CPU/GPU attribution" };
            double previous = -1;
            foreach (var sample in capture.samples)
            {
                if (sample == null || float.IsNaN(sample.frameMs) || float.IsInfinity(sample.frameMs) || sample.frameMs <= 0 ||
                    double.IsNaN(sample.elapsedSeconds) || double.IsInfinity(sample.elapsedSeconds) || sample.elapsedSeconds <= previous ||
                    sample.unityAllocatedBytes < 0 || sample.residentBytes < -1 || float.IsNaN(sample.thermalCelsius) || float.IsInfinity(sample.thermalCelsius) || sample.thermalCelsius < -1)
                    throw new ArgumentException("Invalid or unordered measurement sample.");
                if ((sample.residentBytes >= 0 || sample.thermalCelsius >= 0) && string.IsNullOrWhiteSpace(sample.externalMetricSource))
                    throw new ArgumentException("External measurements require provenance.");
                if (sample.residentBytes >= 0) result.residentSamples++;
                if (sample.thermalCelsius >= 0) result.thermalSamples++;
                previous = sample.elapsedSeconds; frames.Add(sample.frameMs);
                if (sample.frameMs > 2 * 1000.0 / capture.targetFps) result.hitches++;
                result.peakUnityAllocatedBytes = Math.Max(result.peakUnityAllocatedBytes, sample.unityAllocatedBytes);
                result.peakResidentBytes = Math.Max(result.peakResidentBytes, sample.residentBytes);
                result.peakThermalCelsius = Math.Max(result.peakThermalCelsius, sample.thermalCelsius);
            }
            result.durationSeconds = capture.samples[capture.samples.Count - 1].elapsedSeconds - capture.samples[0].elapsedSeconds;
            result.hitchRate = (double)result.hitches / result.frames;
            result.memoryBudgetStatus = capture.memoryBudgetBytes <= 0 ? "Unknown: no memory budget" :
                result.peakResidentBytes < 0 ? "Unknown: resident memory unavailable" :
                result.peakResidentBytes > capture.memoryBudgetBytes ? "Observed resident peak exceeds configured budget" : "Sampled resident peak within budget; unobserved peaks remain unknown";
            frames.Sort();
            result.p50Ms = Percentile(frames, .50); result.p95Ms = Percentile(frames, .95); result.p99Ms = Percentile(frames, .99); result.maxMs = frames[frames.Count-1];
            return result;
        }
        private static double Percentile(List<double> sorted, double p) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * p)-1)];
        public static CaptureComparison Compare(DeviceCapture before, DeviceCapture after)
        {
            var result = new CaptureComparison { before = Summarize(before), after = Summarize(after), status = "Comparable recorded context; causal improvement not proven", scope = "Nearest-rank percentiles; hitch > 2x target frame budget; sampled peaks can miss between-sample peaks" };
            if (new[] { before.scenario, before.device, before.operatingSystem, before.project, before.unityVersion, before.graphicsApi, before.quality,
                    after.scenario, after.device, after.operatingSystem, after.project, after.unityVersion, after.graphicsApi, after.quality }.Any(string.IsNullOrWhiteSpace) ||
                before.scenario != after.scenario || before.device != after.device || before.operatingSystem != after.operatingSystem ||
                before.graphicsApi != after.graphicsApi || before.quality != after.quality || before.project != after.project ||
                before.unityVersion != after.unityVersion || before.warmupSeconds != after.warmupSeconds || before.targetFps != after.targetFps || before.memoryBudgetBytes != after.memoryBudgetBytes || before.isEditor != after.isEditor)
                throw new ArgumentException("Capture contexts differ or required context is missing.");
            if (before.loading == null || after.loading == null) throw new ArgumentException("Missing loading measurement list.");
            foreach (var load in before.loading.Concat(after.loading))
                if (load == null || string.IsNullOrWhiteSpace(load.name) || double.IsNaN(load.milliseconds) || double.IsInfinity(load.milliseconds) || load.milliseconds < 0)
                    throw new ArgumentException("Invalid loading measurement.");
            if (before.loading.Select(x => x.name).Distinct().Count() != before.loading.Count || after.loading.Select(x => x.name).Distinct().Count() != after.loading.Count)
                throw new ArgumentException("Loading interval names must be unique within a capture.");
            foreach (var load in before.loading)
            {
                var match = after.loading.FirstOrDefault(x => x.name == load.name);
                if (match != null) result.loading.Add(new LoadingDelta { name = load.name, beforeMs = load.milliseconds, afterMs = match.milliseconds, deltaMs = match.milliseconds - load.milliseconds });
            }
            if (result.loading.Count == 0) result.limitations.Add("No matched loading measurements.");
            if (result.before.frames != result.after.frames || Math.Abs(result.before.durationSeconds - result.after.durationSeconds) > 1)
                result.limitations.Add("Sampling duration/count differs; hitch counts must not be compared directly.");
            var beforeSources = before.samples.Where(x => !string.IsNullOrEmpty(x.externalMetricSource)).Select(x => x.externalMetricSource).Distinct().OrderBy(x => x);
            var afterSources = after.samples.Where(x => !string.IsNullOrEmpty(x.externalMetricSource)).Select(x => x.externalMetricSource).Distinct().OrderBy(x => x);
            if (!beforeSources.SequenceEqual(afterSources)) result.limitations.Add("External measurement sources differ; resident/thermal deltas are not comparable.");
            result.p95DeltaMs = result.after.p95Ms - result.before.p95Ms;
            if (before.isEditor) result.limitations.Add("Editor results cannot satisfy device acceptance.");
            if (result.before.peakResidentBytes < 0 || result.after.peakResidentBytes < 0) result.limitations.Add("Resident memory unavailable in at least one capture.");
            if (result.before.peakThermalCelsius < 0 || result.after.peakThermalCelsius < 0) result.limitations.Add("Thermal data unavailable in at least one capture.");
            result.limitations.Add("Match input replay, warmup, duration, power state and repeated runs before attributing differences to optimization.");
            return result;
        }
    }
}
