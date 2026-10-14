using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Optifunity.Editor.Module5;

namespace Optifunity.Editor.Core
{
    [InitializeOnLoad]
    public static class UnityMcpBridge
    {
        [Serializable] public class Connection
        {
            public string url, token, projectPath, instanceId;
        }
        [Serializable] private class Payload
        {
            public AssistantContext assistant;
            public string error, status, project, projectPath, instanceId, unityVersion, scope, sceneName, path;
            public string activeRenderPipeline, editorPlatform, buildTarget, graphicsApi;
            public int rendererCount, estimatedTriangles, estimatedVertices, unreadableMeshes, frameIndex;
            public float totalCpuTimeMs;
            public long totalGcAllocBytes;
            public List<Sample> samples;
        }
        [Serializable] private class Sample
        {
            public string name;
            public float durationMs;
            public long gcAllocBytes;
        }
        private sealed class Pending
        {
            public string Path;
            public DateTime Deadline;
            public TaskCompletionSource<string> Result = new TaskCompletionSource<string>();
        }
        private static readonly ConcurrentQueue<Pending> Queue = new ConcurrentQueue<Pending>();
        private static HttpListener listener;
        private static Connection connection;
        private static string discoveryPath;

        static UnityMcpBridge()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            Start();
        }

        private static void Start()
        {
            try
            {
                connection = new Connection {
                    token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                    projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                    instanceId = Guid.NewGuid().ToString("N")
                };
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    var reservation = new TcpListener(IPAddress.Loopback, 0);
                    reservation.Start();
                    int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
                    reservation.Stop();
                    connection.url = $"http://localhost:{port}";
                    listener = new HttpListener();
                    listener.Prefixes.Add(connection.url + "/");
                    try { listener.Start(); break; }
                    catch (HttpListenerException) { listener.Close(); listener = null; }
                }
                if (listener == null) throw new IOException("Cannot allocate a loopback bridge endpoint.");
                discoveryPath = Path.Combine(connection.projectPath, "Library",
                    $"OptifunityBridge-{System.Diagnostics.Process.GetCurrentProcess().Id}.json");
                File.WriteAllText(discoveryPath, JsonUtility.ToJson(connection, true));
                listener.BeginGetContext(Receive, listener);
            }
            catch (Exception ex) { Stop(); Debug.LogWarning("[Optifunity] Bridge unavailable: " + ex.Message); }
        }

        public static bool MatchesIdentity(Connection expected, string token, string project, string instance) =>
            expected != null && !string.IsNullOrEmpty(expected.token) && token == expected.token &&
            project == expected.projectPath && instance == expected.instanceId;

        private static void Receive(IAsyncResult ar)
        {
            var server = (HttpListener)ar.AsyncState;
            HttpListenerContext context;
            try { context = server.EndGetContext(ar); server.BeginGetContext(Receive, server); }
            catch (ObjectDisposedException) { return; }
            catch (HttpListenerException) { return; }
            _ = Handle(context);
        }

        private static async Task Handle(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                if (!MatchesIdentity(connection, request.Headers["X-Optifunity-Token"],
                    Uri.UnescapeDataString(request.Headers["X-Optifunity-Project"] ?? ""), request.Headers["X-Optifunity-Instance"]))
                { Send(context, 403, JsonUtility.ToJson(new Payload { error = "Project/instance authentication failed." })); return; }
                string path = request.Url.AbsolutePath;
                if (path == "/optimize")
                { Send(context, 403, JsonUtility.ToJson(new Payload { error = "Automatic mutation disabled: usage and rollback preconditions are unverified." })); return; }
                if (request.HttpMethod != "GET")
                { Send(context, 405, JsonUtility.ToJson(new Payload { error = "Read-only GET endpoints." })); return; }
                if (path != "/status" && path != "/scene" && path != "/profiler" && path != "/project-settings" && path != "/assistant-context")
                { Send(context, 404, JsonUtility.ToJson(new Payload { error = "Unknown endpoint." })); return; }
                if (Queue.Count >= 64) { Send(context, 429, "{\"error\":\"Bridge queue full.\"}"); return; }
                var pending = new Pending { Path = path, Deadline = DateTime.UtcNow.AddSeconds(4) };
                Queue.Enqueue(pending);
                if (await Task.WhenAny(pending.Result.Task, Task.Delay(4000)) != pending.Result.Task)
                {
                    pending.Result.TrySetCanceled();
                    Send(context, 504, JsonUtility.ToJson(new Payload { error = "Request expired; no mutation performed." }));
                    return;
                }
                Send(context, 200, await pending.Result.Task);
            }
            catch (Exception) { Send(context, 503, "{\"error\":\"Bridge stopped or request failed.\"}"); }
        }

        private static void Update()
        {
            // Bound work per editor update so a burst of clients cannot drain an unbounded queue in one frame.
            for (int i = 0; i < 8 && Queue.TryDequeue(out var pending); i++)
            {
                if (pending.Result.Task.IsCompleted || DateTime.UtcNow >= pending.Deadline)
                { pending.Result.TrySetCanceled(); continue; }
                try { pending.Result.TrySetResult(JsonUtility.ToJson(Read(pending.Path))); }
                catch (Exception ex) { pending.Result.TrySetResult(JsonUtility.ToJson(new Payload { error = ex.Message })); }
            }
        }

        private static Payload Read(string path)
        {
            var payload = new Payload { status = "ok", project = Application.productName, projectPath = connection.projectPath, instanceId = connection.instanceId, unityVersion = Application.unityVersion };
            if (path == "/assistant-context") payload.assistant = AssistantEvidence.Read(connection.instanceId);
            else if (path == "/scene")
            {
                var scene = SceneManager.GetActiveScene();
                payload.scope = "Active scene; includes inactive renderers; geometry is referenced mesh counts, not rendered frame counts.";
                payload.sceneName = scene.name; payload.path = scene.path;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    payload.rendererCount++;
                    var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    payload.estimatedVertices += mesh.vertexCount;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        if (mesh.GetTopology(sub) == MeshTopology.Triangles)
                            payload.estimatedTriangles += (int)(mesh.GetIndexCount(sub) / 3);
                }
            }
            else if (path == "/profiler")
            {
                var frame = FrameDataReader.ReadFrame(ProfilerHelper.GetSelectedFrame());
                if (!frame.IsValid) return new Payload { error = frame.ErrorMessage };
                payload.scope = "Main Thread hierarchy depth 0-10; allocation is not collection; response does not include GPU timing.";
                payload.frameIndex = frame.FrameIndex; payload.totalCpuTimeMs = frame.TotalCpuTimeMs;
                payload.totalGcAllocBytes = frame.TotalGCAllocBytes;
                payload.samples = frame.TopLevelSamples.SelectMany(x => x.Flatten()).OrderByDescending(x => x.TotalTimeMs)
                    .Take(15).Select(x => new Sample { name = x.Name, durationMs = x.TotalTimeMs, gcAllocBytes = x.GCAllocBytes }).ToList();
            }
            else if (path == "/project-settings")
            {
                payload.activeRenderPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name ?? "Built-in";
                payload.editorPlatform = Application.platform.ToString();
                payload.buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
                payload.graphicsApi = string.Join(", ", PlayerSettings.GetGraphicsAPIs(EditorUserBuildSettings.activeBuildTarget));
            }
            return payload;
        }

        private static void Send(HttpListenerContext context, int status, string json)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.Close();
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        }

        private static void Stop()
        {
            listener?.Close(); listener = null;
            while (Queue.TryDequeue(out var pending)) pending.Result.TrySetCanceled();
            if (discoveryPath != null && File.Exists(discoveryPath)) File.Delete(discoveryPath);
        }
    }
}
