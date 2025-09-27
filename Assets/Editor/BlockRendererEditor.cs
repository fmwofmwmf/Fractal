// using UnityEditor;
// using UnityEngine;
//
// [CustomEditor(typeof(BlockRenderer))]
// public class MyScriptEditor3 : Editor
// {
//     public override void OnInspectorGUI()
//     {
//         // Draw the normal inspector
//         DrawDefaultInspector();
//
//         // Add a button
//         BlockRenderer script = (BlockRenderer)target;
//         if (GUILayout.Button("Eval"))
//         {
//             script.ReEvaluateTree();
//         }
//     }
// }