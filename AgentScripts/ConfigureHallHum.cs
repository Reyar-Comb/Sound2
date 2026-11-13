using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class ConfigureHallHum
{
    public static string Main()
    {
        var scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/CircularHall.unity")
            throw new InvalidOperationException("Open CircularHall in Edit mode.");
        var p = UnityEngine.Object.FindAnyObjectByType<BouncePlaytest>();
        var existing = UnityEngine.Object.FindAnyObjectByType<HallHumEmitter>();
        HallHumEmitter hum = existing;
        if (hum == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Hall Center Hum (H)";
            go.layer = 2;
            go.transform.position = new Vector3(0, 1.2f, 0);
            go.transform.localScale = Vector3.one * 0.18f;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            const string materialPath = "Assets/Materials/CircularHall/HumMarker.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(0.15f, 0.8f, 0.7f));
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(0.05f, 0.4f, 0.3f));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            go.GetComponent<Renderer>().sharedMaterial = material;
            hum = go.AddComponent<HallHumEmitter>();
        }
        hum.controls = p;
        p.hallHum = hum;
        p.enhancedAttenuation = true;
        var settings = SteamAudio.SteamAudioSettings.Singleton;
        settings.realTimeMaxSources = Mathf.Max(33, settings.realTimeMaxSources);
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(hum);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = p.gameObject;
        return "Saved center hum at (0,1.2,0), 110Hz + 0.6Hz amplitude LFO; editable curve affects ONLY impacts. Physical hum falloff remains separate.";
    }
}
