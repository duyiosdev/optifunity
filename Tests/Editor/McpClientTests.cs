using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using Optifunity.Editor.Core;

namespace Optifunity.Tests
{
    public class McpClientTests
    {
        [UnityTest] public IEnumerator NodeClientUsesSelectedEditor() => RunClient("node", "McpServer/index.js");
        [UnityTest] public IEnumerator PythonClientUsesSelectedEditor()
        {
            string python = Environment.GetEnvironmentVariable("OPTIFUNITY_TEST_PYTHON");
            if (string.IsNullOrEmpty(python)) Assert.Ignore("Set OPTIFUNITY_TEST_PYTHON to run the Python live-client test.");
            return RunClient(python, "McpServer/unity_mcp_server.py");
        }
        private static IEnumerator RunClient(string executable, string relativePath)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnityMcpBridge).Assembly);
            string descriptor = (string)typeof(UnityMcpBridge).GetField("discoveryPath", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var start = new ProcessStartInfo(executable, "\"" + Path.Combine(package.resolvedPath, relativePath) + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.EnvironmentVariables["OPTIFUNITY_CONNECTION"] = descriptor;
            using (var process = Process.Start(start))
            {
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"validation\",\"version\":\"1\"}}}");
                    process.StandardInput.Flush();
                    var line = process.StandardOutput.ReadLineAsync();
                    var deadline = EditorApplication.timeSinceStartup + 12;
                    while (!line.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
                    Assert.That(line.IsCompleted, "Client initialize timed out"); Assert.That(line.Result, Does.Contain("\"result\""));
                    process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                    process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"unity_get_status\",\"arguments\":{}}}");
                    process.StandardInput.Flush();
                    line = process.StandardOutput.ReadLineAsync(); deadline = EditorApplication.timeSinceStartup + 12;
                    while (!line.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
                    Assert.That(line.IsCompleted, "Client status timed out");
                    Assert.That(line.Result, Does.Contain("unityVersion"));
                    var identity = (UnityMcpBridge.Connection)typeof(UnityMcpBridge).GetField("connection", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                    Assert.That(line.Result, Does.Contain(identity.instanceId));
                    Assert.That(line.Result, Does.Not.Contain("\"isError\":true"));
                    Assert.That(line.Result, Does.Not.Contain("\"isError\": true"));
                    process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"unity_apply_asset_optimization\",\"arguments\":{\"assetPath\":\"Assets/never-write\",\"type\":\"mesh\"}}}");
                    process.StandardInput.Flush();
                    line = process.StandardOutput.ReadLineAsync(); deadline = EditorApplication.timeSinceStartup + 12;
                    while (!line.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
                    Assert.That(line.IsCompleted); Assert.That(line.Result, Does.Contain("isError"));
                    Assert.That(line.Result, Does.Match("\"isError\"\\s*:\\s*true"));
                }
                finally { if (!process.HasExited) process.Kill(); }
            }
        }
    }
}
