using System;
using System.Collections.Generic;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ConfigureVineMotion
{
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Exit play mode before configuration");
        const string path = "Assets/Prefabs/RedVineArea.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var audio = prefab.GetComponent<MonsterAudioEmitter>();
            if (audio == null) audio = prefab.AddComponent<MonsterAudioEmitter>();
            audio.profile = AssetDatabase.LoadAssetAtPath<MonsterAudioProfile>("Assets/Resources/MonsterAudio.asset");
            PrefabUtility.SaveAsPrefabAsset(prefab, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var configured = new List<object>();
        foreach (var area in UnityEngine.Object.FindObjectsByType<MonsterVineArea>(FindObjectsInactive.Include))
        {
            if (!area.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(area.gameObject.scene)) continue;
            var motion = area.GetComponent<MonsterVineAreaMotion>();
            if (motion == null) motion = Undo.AddComponent<MonsterVineAreaMotion>(area.gameObject);
            var audio = area.GetComponent<MonsterAudioEmitter>();
            if (audio == null) audio = Undo.AddComponent<MonsterAudioEmitter>(area.gameObject);
            EditorSceneManager.MarkSceneDirty(area.gameObject.scene);
            configured.Add(new { area = area.name, scene = area.gameObject.scene.path,
                start = motion.startPosition.ToString(), end = motion.endPosition.ToString(), speed = motion.speed });
        }
        // Scene saving remains explicit so the designer can review their current unsaved edits.
        return new { prefab = path, configured };
    }
}
