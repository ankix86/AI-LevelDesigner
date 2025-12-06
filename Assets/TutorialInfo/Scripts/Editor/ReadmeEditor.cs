using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Readme))]
public class ReadmeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Minimal inspector to satisfy reference; customize as needed.
        DrawDefaultInspector();
    }
}
