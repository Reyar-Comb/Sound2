using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    // Keep cuts correct for Hierarchy deletion, Inspector edits and Undo even with the designer closed.
    [InitializeOnLoad]
    public static class EmbeddedDoorEditorSync
    {
        static readonly Dictionary<LevelLayout, string> signatures = new();
        static readonly HashSet<LevelLayout> failed = new();
        static double nextCheck;
        static bool pending = true;
        static EmbeddedDoorEditorSync()
        {
            EditorApplication.update += Update;
            EditorApplication.hierarchyChanged += () => pending = true;
            Undo.undoRedoPerformed += () => pending = true;
            ObjectChangeEvents.changesPublished += OnChanges;
            EditorSceneManager.sceneOpened += (_, _) => pending = true;
            EditorApplication.playModeStateChanged += _ => pending = true;
        }
        static void OnChanges(ref ObjectChangeEventStream changes) => pending = true;
        public static void Remember(LevelLayout layout)
        {
            failed.Remove(layout);
            var doors = EmbeddedDoorPlacement.Doors(layout);
            if (doors.Length > 0) signatures[layout] = Signature(layout, doors);
            else signatures.Remove(layout);
        }
        public static void EnsureCurrent(LevelLayout layout)
        {
            var doors = EmbeddedDoorPlacement.Doors(layout);
            if (doors.Length == 0 && !signatures.ContainsKey(layout)) return;
            if (failed.Contains(layout) || !signatures.TryGetValue(layout, out var previous) || previous != Signature(layout, doors))
                LevelBuilder.Rebuild(layout);
        }
        static string Signature(LevelLayout layout, EmbeddedDoor[] doors)
        {
            var value = new StringBuilder();
            value.Append(layout.wallHeight).Append('|').Append(layout.wallThickness).Append('|').Append(layout.architecturalPartitions);
            value.Append(layout.transform.localToWorldMatrix.ToString());
            foreach (var room in layout.rooms) value.Append(JsonUtility.ToJson(room));
            foreach (var partition in layout.partitions) value.Append(JsonUtility.ToJson(partition));
            foreach (var corridor in layout.corridors) value.Append(JsonUtility.ToJson(corridor));
            foreach (var pit in layout.pits) value.Append(JsonUtility.ToJson(pit));
            foreach (var opening in layout.doorways) value.Append(JsonUtility.ToJson(opening));
            foreach (var door in doors)
                value.Append(door.GetEntityId().ToString()).Append(JsonUtility.ToJson(door)).Append(door.transform.localToWorldMatrix.ToString("F5"));
            return value.ToString();
        }
        static void Update()
        {
            if (!pending || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || EditorApplication.timeSinceStartup < nextCheck) return;
            pending = false;
            nextCheck = EditorApplication.timeSinceStartup + .25;
            var stale = new List<LevelLayout>();
            foreach (var entry in signatures) if (entry.Key == null) stale.Add(entry.Key);
            foreach (var key in stale) { signatures.Remove(key); failed.Remove(key); }
            foreach (var layout in Resources.FindObjectsOfTypeAll<LevelLayout>())
            {
                if (EditorUtility.IsPersistent(layout) || !layout.gameObject.scene.IsValid()
                    || EditorSceneManager.IsPreviewScene(layout.gameObject.scene)) continue;
                var doors = EmbeddedDoorPlacement.Doors(layout);
                if (doors.Length == 0 && !signatures.ContainsKey(layout)) continue;
                var signature = Signature(layout, doors);
                if (signatures.TryGetValue(layout, out var previous) && previous == signature) continue;
                signatures[layout] = signature;
                try
                {
                    LevelBuilder.Rebuild(layout);
                    signatures[layout] = Signature(layout, doors);
                    if (doors.Length == 0) signatures.Remove(layout);
                }
                catch (System.Exception error) { failed.Add(layout); Debug.LogWarning(error.Message, layout); }
            }
        }
    }
}
