using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ShellRenderer))]
public class MyScriptEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the normal inspector
        DrawDefaultInspector();

        // Add a button
        ShellRenderer script = (ShellRenderer)target;
        if (GUILayout.Button("Eval"))
        {
            script.ReEvaluateTree();
        }
    }
}