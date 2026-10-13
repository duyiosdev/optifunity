using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Optifunity.Editor.Module6;

namespace Optifunity.Tests
{
    public static class BFixtureHarness
    {
        public static void Build()
        {
            if (!File.Exists(".optifunity-fixture")) throw new InvalidOperationException("Owned disposable fixture required.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Build dependency fixture";
            const string path = "Assets/OptifunityBValidatedScene.unity";
            EditorSceneManager.SaveScene(scene, path);
            Directory.CreateDirectory("BPlayer");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { path }, locationPathName = "BPlayer/OptifunityFixture.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development | BuildOptions.DetailedBuildReport });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Fixture build failed: " + report.summary.result);
            var inventory = ProjectInventoryScanner.Scan(false, selectedPaths: new[] { path });
            ProjectInventoryScanner.ReadBuildReport(inventory, report);
            if (inventory.packedAssets.Count == 0) throw new InvalidOperationException("No packed build evidence returned.");
            File.WriteAllText("b-build-evidence.json", JsonUtility.ToJson(inventory, true));
        }
    }
}
