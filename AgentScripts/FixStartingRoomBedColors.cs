using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FixStartingRoomBedColors
{
    public static object Run()
    {
        if (Application.isPlaying) throw new Exception("Run in Edit Mode.");
        var opening = UnityEngine.Object.FindAnyObjectByType<SoundShellOpening>();
        if (opening == null || opening.gameObject.scene.path != "Assets/Scenes/SoundAndShell.unity")
            throw new Exception("Open SoundAndShell first.");
        var scene = opening.gameObject.scene;
        if (scene.isDirty) throw new Exception("Save existing scene changes first.");
        Directory.CreateDirectory("Logs/BedColorFix");
        if (!File.Exists("Logs/BedColorFix/before.unity"))
            EditorSceneManager.SaveScene(scene, "Logs/BedColorFix/before.unity", true);
        const string folder = "Assets/Materials/StartingRoomBed";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Materials", "StartingRoomBed");
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) throw new Exception("Missing URP Unlit shader.");
        var bed = opening.startingRoom.GetComponentInChildren<StartingRoomBed>(true);
        // A null override opts this prop out of LevelProp's black-material/combined-mesh path.
        var prop = bed.GetComponent<Sound2.LevelEditing.LevelProp>();
        Undo.RecordObject(prop, "Keep starting-room bed original materials");
        prop.gameplayMaterial = null;
        PrefabUtility.RecordPrefabInstancePropertyModifications(prop);
        foreach (var renderer in bed.GetComponentsInChildren<Renderer>(true))
        {
            // Source prefab remains lit for the other levels; only this room uses albedo directly.
            var sourceRenderer = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
            var sources = sourceRenderer.sharedMaterials;
            var materials = new Material[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                string path = folder + "/" + source.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = source.name };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                material.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }
            Undo.RecordObject(renderer, "Restore starting-room bed colors");
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        foreach (var material in bed.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials))
            AssetDatabase.SaveAssetIfDirty(material);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return bed.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
            .Select(m => new { m.name, shader = m.shader.name, color = m.GetColor("_BaseColor").ToString() }).ToArray();
    }
}
