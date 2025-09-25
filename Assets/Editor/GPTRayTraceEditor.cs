using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BlockRendererNew))]
public class MyScriptEditor2 : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the normal inspector
        DrawDefaultInspector();

        // Add a button
        BlockRendererNew script = (BlockRendererNew)target;
        if (GUILayout.Button("Eval"))
        {
            script.ReDraw();
        }
    }
}