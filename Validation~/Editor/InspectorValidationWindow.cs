using System;
using System.Collections.Generic;
using System.IO;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class InspectorValidationWindow : EditorWindow
{
    private UnityEngine.Object[] targets;
    private UnityEditor.Editor inspector;
    private int index;
    private bool advance;
    private bool failed;
    private Vector2 scroll;
    private readonly List<string> results = new List<string>();

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Validation/Loopback.unity");
        var window = GetWindow<InspectorValidationWindow>();
        window.targets = new UnityEngine.Object[] {
            FindObjectOfType<TSMPSetup>(), FindObjectOfType<TSMPEncoder>(), FindObjectOfType<TSMPDecoder>(),
            FindObjectOfType<TSMPNetworkTransformSync>(), FindObjectOfType<TSMPNetworkHumanoidPoseSync>(), FindObjectOfType<LoopbackProbe>()
        };
        window.position = new Rect(50, 50, 520, 850);
        window.Show();
        EditorApplication.update += window.Tick;
        Application.logMessageReceived += window.OnLog;
    }

    private void Tick()
    {
        if (advance)
        {
            advance = false;
            if (inspector != null) DestroyImmediate(inspector);
            inspector = null;
            index++;
        }
        if (index >= targets.Length)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), (failed ? "FAIL" : "PASS") + "\n" + string.Join("\n", results));
            EditorApplication.Exit(failed ? 1 : 0);
            return;
        }
        if (inspector == null) inspector = UnityEditor.Editor.CreateEditor(targets[index]);
        Repaint();
    }

    private void OnGUI()
    {
        if (inspector == null || advance) return;
        scroll = EditorGUILayout.BeginScrollView(scroll);
        inspector.OnInspectorGUI();
        EditorGUILayout.EndScrollView();
        if (Event.current.type != EventType.Repaint) return;
        results.Add(targets[index].GetType().Name + " -> " + inspector.GetType().Name);
        advance = true;
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        failed = true;
        results.Add(type + ": " + message);
    }
}
