using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;

// Upgrade existing authored scenes using the same rebuild/export path as future maps.
public static class ConfigureLevelPresentation
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Open scenes contain unsaved edits.");
        var reports = new List<object>();
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var layouts = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelLayout>(true)).ToArray();
                    if (layouts.Length == 0) continue;
                    foreach (var layout in layouts)
                    {
                        LevelBuilder.Rebuild(layout);
                        LevelBuilder.Persist(layout, path);
                    }
                    var players = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).ToArray();
                    foreach (var player in players) LevelBuilder.ConfigurePointCloudPlayer(player.gameObject);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + path);
                    reports.Add(new { scene = path, layouts = layouts.Length, players = players.Length,
                        ceilings = layouts.Count(l => l.generatedRoot.transform.Find("Ceiling") != null),
                        previewOpen = layouts.All(l => !l.generatedRoot.transform.Find("Ceiling").gameObject.activeSelf) });
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        return reports;
    }
}
