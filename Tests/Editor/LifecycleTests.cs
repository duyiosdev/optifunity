using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using Optifunity.Editor.Core;
using Optifunity.Editor.Module1;
using Optifunity.Editor.Module5;

namespace Optifunity.Tests
{
    public class LifecycleTests
    {
        [Test] public void OrdinaryMethodNamedUpdateIsNotUnityCallback()
        {
            SemanticCodeAnalyzer.Clear();
            Assert.That(GCAllocAnalyzer.Analyze("Plain.cs", new[] { "class Plain { void Update() { var x = new int[4]; } }" }), Is.Empty);
        }
        [Test] public void InclusiveMarkerDoesNotDoubleCountItsChild()
        {
            var child = new ProfilerSample { Name = "GC.Collect", TotalTimeMs = 2 };
            var parent = new ProfilerSample { Name = "GC.Collect", TotalTimeMs = 3 }; parent.Children.Add(child);
            var frame = new FrameSnapshot(); frame.TopLevelSamples.Add(parent);
            Assert.That(frame.SumTime("GC.Collect"), Is.EqualTo(3));
        }
        [Test] public void T20_ReloadRotatesIdentityAndRemovesDescriptor()
        {
            var type = typeof(UnityMcpBridge);
            var descriptorField = type.GetField("discoveryPath", BindingFlags.NonPublic | BindingFlags.Static);
            var identityField = type.GetField("connection", BindingFlags.NonPublic | BindingFlags.Static);
            string path = (string)descriptorField.GetValue(null);
            var old = (UnityMcpBridge.Connection)identityField.GetValue(null);
            try
            {
                type.GetMethod("Stop", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                Assert.That(System.IO.File.Exists(path), Is.False);
            }
            finally { type.GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); }
            var fresh = (UnityMcpBridge.Connection)identityField.GetValue(null);
            Assert.That(fresh.instanceId, Is.Not.EqualTo(old.instanceId));
            Assert.That(UnityMcpBridge.MatchesIdentity(fresh, old.token, old.projectPath, old.instanceId), Is.False);
        }
        [UnityTest] public IEnumerator T20_ExpiredQueueDoesNotExecuteAfterEditorResumes()
        {
            var type = typeof(UnityMcpBridge);
            var update = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction),
                type.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static));
            var connection = (UnityMcpBridge.Connection)type.GetField("connection", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            EditorApplication.update -= update;
            try
            {
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(connection.url + "/status");
                request.Headers["X-Optifunity-Token"] = connection.token;
                request.Headers["X-Optifunity-Project"] = Uri.EscapeDataString(connection.projectPath);
                request.Headers["X-Optifunity-Instance"] = connection.instanceId;
                var response = request.GetResponseAsync();
                double deadline = EditorApplication.timeSinceStartup + 10;
                while (!response.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(response.IsCompleted, "Timeout response missing");
                Assert.That(response.IsFaulted);
                var error = response.Exception.Flatten().InnerExceptions.OfType<System.Net.WebException>().Single();
                using (var http = (System.Net.HttpWebResponse)error.Response) Assert.That((int)http.StatusCode, Is.EqualTo(504));
                var queue = type.GetField("Queue", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                var pending = ((System.Collections.IEnumerable)queue).Cast<object>().ToArray();
                Assert.That(pending.Length, Is.GreaterThan(0));
                foreach (var item in pending)
                {
                    var completion = item.GetType().GetField("Result").GetValue(item);
                    var task = (Task)completion.GetType().GetProperty("Task").GetValue(completion);
                    Assert.That(task.IsCanceled, "Expired request must already be canceled");
                }
                update();
                Assert.That(((System.Collections.IEnumerable)queue).Cast<object>(), Is.Empty);
            }
            finally { EditorApplication.update += update; }
        }
    }
}
