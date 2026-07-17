using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Optifunity.Editor.Module5;

namespace Optifunity.Editor.Core
{
    [InitializeOnLoad]
    public static class UnityMcpBridge
    {
        private static HttpListener _listener;
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private const int PORT = 9090;

        static UnityMcpBridge()
        {
            // Register Update to execute the queue on Main Thread
            EditorApplication.update += Update;

            // Start HTTP Server on background thread
            StartServer();
        }

        private static void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private static void StartServer()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{PORT}/");
                _listener.Start();
                _listener.BeginGetContext(OnRequestReceived, null);
                Debug.Log($"<color=#00FF7F>[Optifunity Bridge]</color> HTTP Server started on port {PORT}. Ready for AI commands.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Optifunity Bridge] Failed to start server: {ex.Message}");
            }
        }

        private static void OnRequestReceived(IAsyncResult ar)
        {
            if (_listener == null || !_listener.IsListening) return;

            HttpListenerContext context = null;
            try
            {
                context = _listener.EndGetContext(ar);
                // Keep listening
                _listener.BeginGetContext(OnRequestReceived, null);
            }
            catch
            {
                return;
            }

            if (context == null) return;

            // Handle request asynchronously
            Task.Run(async () =>
            {
                try
                {
                    await HandleRequestAsync(context);
                }
                catch (Exception ex)
                {
                    SendJsonResponse(context, 500, $"{{\"error\": \"{ex.Message}\"}}");
                }
            });
        }

        private static async Task HandleRequestAsync(HttpListenerContext context)
        {
            string rawPath = context.Request.Url.AbsolutePath.ToLower();
            string method = context.Request.HttpMethod.ToUpper();

            if (rawPath == "/status")
            {
                SendJsonResponse(context, 200, $"{{\"status\": \"ok\", \"project\": \"{Application.productName}\", \"unity_version\": \"{Application.unityVersion}\"}}");
                return;
            }

            // Main Thread Endpoints:
            if (rawPath == "/scene")
            {
                var tcs = new TaskCompletionSource<string>();
                _mainThreadQueue.Enqueue(() =>
                {
                    try
                    {
                        var activeScene = SceneManager.GetActiveScene();
                        var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                        var triangleCount = 0;
                        var vertexCount = 0;
                        var skinnedMeshCount = 0;
                        var meshRendererCount = 0;
                        var lodGroups = UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length;

                        foreach (var r in allRenderers)
                        {
                            if (r is SkinnedMeshRenderer smr)
                            {
                                skinnedMeshCount++;
                                if (smr.sharedMesh != null)
                                {
                                    triangleCount += smr.sharedMesh.triangles.Length / 3;
                                    vertexCount += smr.sharedMesh.vertexCount;
                                }
                            }
                            else if (r is MeshRenderer mr)
                            {
                                meshRendererCount++;
                                var filter = mr.GetComponent<MeshFilter>();
                                if (filter != null && filter.sharedMesh != null)
                                {
                                    triangleCount += filter.sharedMesh.triangles.Length / 3;
                                    vertexCount += filter.sharedMesh.vertexCount;
                                }
                            }
                        }

                        string json = $"{{" +
                                      $"\"sceneName\": \"{activeScene.name}\"," +
                                      $"\"path\": \"{activeScene.path}\"," +
                                      $"\"rendererCount\": {allRenderers.Length}," +
                                      $"\"meshRendererCount\": {meshRendererCount}," +
                                      $"\"skinnedMeshRendererCount\": {skinnedMeshCount}," +
                                      $"\"lodGroups\": {lodGroups}," +
                                      $"\"estimatedTriangles\": {triangleCount}," +
                                      $"\"estimatedVertices\": {vertexCount}" +
                                      $"}}";
                        tcs.SetResult(json);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });

                string res = await tcs.Task;
                SendJsonResponse(context, 200, res);
                return;
            }

            if (rawPath == "/profiler")
            {
                var tcs = new TaskCompletionSource<string>();
                _mainThreadQueue.Enqueue(() =>
                {
                    try
                    {
                        int selectedFrame = ProfilerHelper.GetSelectedFrame();
                        if (selectedFrame < 0)
                        {
                            tcs.SetResult($"{{\"error\": \"No selected frame in Profiler. Please select a frame first.\"}}");
                            return;
                        }

                        var snapshot = FrameDataReader.ReadFrame(selectedFrame);
                        if (snapshot == null || !snapshot.IsValid)
                        {
                            string err = snapshot?.ErrorMessage ?? "Failed to read snapshot.";
                            tcs.SetResult($"{{\"error\": \"{err}\"}}");
                            return;
                        }

                        var allSamples = new List<ProfilerSample>();
                        foreach (var rootSample in snapshot.TopLevelSamples)
                        {
                            allSamples.AddRange(rootSample.Flatten());
                        }

                        // Sort by CPU
                        allSamples.Sort((a, b) => b.TotalTimeMs.CompareTo(a.TotalTimeMs));
                        var cpuSamplesBuilder = new StringBuilder();
                        for (int i = 0; i < Math.Min(15, allSamples.Count); i++)
                        {
                            if (i > 0) cpuSamplesBuilder.Append(",");
                            var s = allSamples[i];
                            cpuSamplesBuilder.Append($"{{" +
                                                      $"\"name\": \"{EscapeJson(s.Name)}\"," +
                                                      $"\"durationMs\": {s.TotalTimeMs:F3}," +
                                                      $"\"calls\": {s.CallCount}," +
                                                      $"\"gcAllocBytes\": {s.GCAllocBytes}" +
                                                      $"}}");
                        }

                        // Sort by GC Alloc
                        var gcSamples = new List<ProfilerSample>(allSamples);
                        gcSamples.Sort((a, b) => b.GCAllocBytes.CompareTo(a.GCAllocBytes));
                        var gcSamplesBuilder = new StringBuilder();
                        var addedCount = 0;
                        for (int i = 0; i < gcSamples.Count && addedCount < 15; i++)
                        {
                            var s = gcSamples[i];
                            if (s.GCAllocBytes <= 0) continue;
                            if (addedCount > 0) gcSamplesBuilder.Append(",");
                            gcSamplesBuilder.Append($"{{" +
                                                     $"\"name\": \"{EscapeJson(s.Name)}\"," +
                                                     $"\"durationMs\": {s.TotalTimeMs:F3}," +
                                                     $"\"calls\": {s.CallCount}," +
                                                     $"\"gcAllocBytes\": {s.GCAllocBytes}" +
                                                     $"}}");
                            addedCount++;
                        }

                        string json = $"{{" +
                                      $"\"frameIndex\": {snapshot.FrameIndex}," +
                                      $"\"totalCpuTimeMs\": {snapshot.TotalCpuTimeMs:F2}," +
                                      $"\"totalGcAllocBytes\": {snapshot.TotalGCAllocBytes}," +
                                      $"\"topCpuSamples\": [{cpuSamplesBuilder}]," +
                                      $"\"topGcAllocSamples\": [{gcSamplesBuilder}]" +
                                      $"}}";
                        tcs.SetResult(json);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });

                string res = await tcs.Task;
                SendJsonResponse(context, 200, res);
                return;
            }

            if (rawPath == "/project-settings")
            {
                var tcs = new TaskCompletionSource<string>();
                _mainThreadQueue.Enqueue(() =>
                {
                    try
                    {
                        var urpAsset = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
                        string urpName = urpAsset != null ? urpAsset.name : "None";

                        string json = $"{{" +
                                      $"\"activeRenderPipeline\": \"{urpName}\"," +
                                      $"\"unityVersion\": \"{Application.unityVersion}\"," +
                                      $"\"platform\": \"{Application.platform}\"," +
                                      $"\"scriptingBackend\": \"{PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)}\"," +
                                      $"\"graphicsApi\": \"{SystemInfo.graphicsDeviceType}\"" +
                                      $"}}";
                        tcs.SetResult(json);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });

                string res = await tcs.Task;
                SendJsonResponse(context, 200, res);
                return;
            }

            if (rawPath == "/optimize" && method == "POST")
            {
                string body = "";
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync();
                }

                var tcs = new TaskCompletionSource<string>();
                _mainThreadQueue.Enqueue(() =>
                {
                    try
                    {
                        if (string.IsNullOrEmpty(body))
                        {
                            tcs.SetResult($"{{\"error\": \"Empty body. Expecting json containing assetPath.\"}}");
                            return;
                        }

                        string assetPath = ExtractJsonField(body, "assetPath");
                        string type = ExtractJsonField(body, "type");

                        if (string.IsNullOrEmpty(assetPath))
                        {
                            tcs.SetResult($"{{\"error\": \"Missing assetPath in payload.\"}}");
                            return;
                        }

                        var importer = AssetImporter.GetAtPath(assetPath);
                        if (importer == null)
                        {
                            tcs.SetResult($"{{\"error\": \"Asset not found at path: {assetPath}\"}}");
                            return;
                        }

                        bool applied = false;
                        if (type == "texture" && importer is TextureImporter texImporter)
                        {
                            texImporter.mipmapEnabled = true;

                            // Android ASTC 8x8 & max 1024
                            TextureImporterPlatformSettings androidSettings = texImporter.GetPlatformTextureSettings("Android");
                            androidSettings.overridden = true;
                            androidSettings.format = TextureImporterFormat.ASTC_8x8;
                            androidSettings.maxTextureSize = Math.Min(androidSettings.maxTextureSize, 1024);
                            texImporter.SetPlatformTextureSettings(androidSettings);

                            // iOS ASTC 8x8
                            TextureImporterPlatformSettings iosSettings = texImporter.GetPlatformTextureSettings("iPhone");
                            iosSettings.overridden = true;
                            iosSettings.format = TextureImporterFormat.ASTC_8x8;
                            iosSettings.maxTextureSize = Math.Min(iosSettings.maxTextureSize, 1024);
                            texImporter.SetPlatformTextureSettings(iosSettings);

                            texImporter.SaveAndReimport();
                            applied = true;
                        }
                        else if (type == "mesh" && importer is ModelImporter modelImporter)
                        {
                            modelImporter.isReadable = false; // Disable Read/Write Enabled
                            modelImporter.animationCompression = ModelImporterAnimationCompression.Optimal;
                            modelImporter.SaveAndReimport();
                            applied = true;
                        }

                        tcs.SetResult($"{{\"success\": {applied.ToString().ToLower()}, \"assetPath\": \"{assetPath}\"}}");
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });

                string res = await tcs.Task;
                SendJsonResponse(context, 200, res);
                return;
            }

            SendJsonResponse(context, 404, $"{{\"error\": \"Endpoint {rawPath} not found.\"}}");
        }

        private static void SendJsonResponse(HttpListenerContext context, int statusCode, string json)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch
            {
                // socket closed or error
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string ExtractJsonField(string json, string fieldName)
        {
            string pattern = $"\"{fieldName}\"\\s*:\\s*\"([^\"]+)\"";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return "";
        }
    }
}
