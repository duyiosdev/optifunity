using System;
using System.IO;
using UnityEngine;
using Optifunity.Runtime;

public sealed class OptifunityPlayerProbe : MonoBehaviour
{
    private DeviceEvidenceRecorder recorder;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartProbe()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "-optifunity-b-probe") < 0) return;
        new GameObject("Optifunity owned fixture probe").AddComponent<OptifunityPlayerProbe>();
    }
    private void Start()
    {
        recorder = gameObject.AddComponent<DeviceEvidenceRecorder>();
        recorder.scenario = "Disposable Windows fixture, not mobile acceptance";
        recorder.variant = "fixture"; recorder.warmupSeconds = 0; recorder.maxSamples = 60;
        recorder.Begin();
    }
    private void Update()
    {
        if (recorder == null || recorder.IsRecording) return;
        string path = recorder.StopAndSave();
        Debug.Log("OPTIFUNITY_CAPTURE=" + path);
        Application.Quit(0);
        enabled = false;
    }
}
