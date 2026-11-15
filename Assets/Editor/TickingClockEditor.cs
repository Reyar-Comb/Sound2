using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TickingClock)), CanEditMultipleObjects]
public sealed class TickingClockEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "tickRecordings");

        var recordings = serializedObject.FindProperty("tickRecordings");
        int previousCount = recordings.arraySize;
        EditorGUILayout.PropertyField(recordings, true);
        for (int i = previousCount; i < recordings.arraySize; i++)
        {
            var entry = recordings.GetArrayElementAtIndex(i);
            var clip = entry.FindPropertyRelative("clip");
            // An empty row slot is zero-initialized by the array drawer.
            if (clip.objectReferenceValue == null) entry.FindPropertyRelative("volume").floatValue = 1;
        }
        serializedObject.ApplyModifiedProperties();
    }
}
