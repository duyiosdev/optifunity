using System;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Optifunity.Editor.Module3;
using Optifunity.Editor.Module5;

namespace Optifunity.Tests
{
    public static class BRuntimeHarness
    {
        private static double started;
        private static bool capturing;
        private static bool oldEnabled, oldEditor;
        public static void Run()
        {
            if (!File.Exists(".optifunity-fixture")) throw new InvalidOperationException("Owned disposable fixture required.");
            oldEnabled = ProfilerDriver.enabled; oldEditor = ProfilerDriver.profileEditor;
            ProfilerDriver.profileEditor = true; ProfilerDriver.enabled = true;
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            if (capturing) return;
            if (EditorApplication.timeSinceStartup - started < 3) return;
            capturing = true; EditorApplication.update -= Tick;
            try
            {
                ProfilerDriver.enabled = false;
                var threads = MultiThreadFrameReader.Read(ProfilerDriver.lastFrameIndex);
                File.WriteAllText("b-thread-evidence.json", JsonUtility.ToJson(threads, true));
                if (threads.threads.Count == 0) throw new InvalidOperationException("No native profiler thread data captured: " + threads.status);
                var before = MemoryEvidenceReader.Read();
                var mesh = new Mesh { name = "B memory fixture" };
                var after = MemoryEvidenceReader.Read();
                File.WriteAllText("b-memory-diff.json", JsonUtility.ToJson(MemoryEvidenceReader.Compare(before, after), true));
                UnityEngine.Object.DestroyImmediate(mesh);
                string path = Path.GetFullPath("b-memory-" + Guid.NewGuid().ToString("N") + ".snap");
                MemoryEvidenceReader.CaptureNative(path, (saved, ok) => {
                    bool valid = ok && File.Exists(saved) && new FileInfo(saved).Length > 0;
                    File.WriteAllText("b-native-snapshot.json", JsonUtility.ToJson(new Receipt { success = valid, bytes = valid ? new FileInfo(saved).Length : 0, filename = Path.GetFileName(saved) }, true));
                    Restore(); EditorApplication.Exit(valid ? 0 : 1);
                });
            }
            catch (Exception ex) { Debug.LogException(ex); Restore(); EditorApplication.Exit(1); }
        }
        private static void Restore() { ProfilerDriver.enabled = oldEnabled; ProfilerDriver.profileEditor = oldEditor; }
        [Serializable] private class Receipt { public bool success; public long bytes; public string filename; }
    }
}
