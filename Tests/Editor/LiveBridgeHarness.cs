using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Optifunity.Tests
{
    // This harness is inert outside an explicitly marked disposable validation project.
    [InitializeOnLoad]
    public static class LiveBridgeHarness
    {
        private const string Marker = "optifunity-live-validation.marker";
        private static double deadline;
        static LiveBridgeHarness()
        {
            if (File.Exists(".optifunity-fixture") && File.Exists(Marker)) EditorApplication.delayCall += Hold;
        }
        public static void Hold()
        {
            if (!File.Exists(".optifunity-fixture")) throw new InvalidOperationException("Disposable fixture required.");
            File.WriteAllText(Marker, "Explicit live bridge validation; remove stop marker to start a new run.");
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            File.WriteAllText("bridge-ready.txt", System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
        }
        private static void Tick()
        {
            if (File.Exists("bridge-stop.txt") || EditorApplication.timeSinceStartup > deadline)
            {
                File.Delete(Marker); File.Delete("bridge-ready.txt"); File.Delete("bridge-stop.txt");
                EditorApplication.update -= Tick; EditorApplication.Exit(0); return;
            }
            if (File.Exists("bridge-reload.txt"))
            {
                File.Delete("bridge-reload.txt"); File.Delete("bridge-ready.txt");
                CompilationPipeline.RequestScriptCompilation();
            }
        }
    }
}
