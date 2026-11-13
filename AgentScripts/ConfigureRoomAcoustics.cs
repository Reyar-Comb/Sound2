using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SteamAudio;

public static class ConfigureRoomAcoustics
{
    public static string Main()
    {
        var scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/CubeRoom.unity" || scene.isDirty)
            throw new InvalidOperationException("Open the saved CubeRoom scene in Edit mode first.");

        const string folder = "Assets/Audio/RoomAcoustics";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Audio", "RoomAcoustics");
        var material = AssetDatabase.LoadAssetAtPath<SteamAudioMaterial>(folder + "/RoomSurfaces.asset");
        if (material == null)
        {
            material = ScriptableObject.CreateInstance<SteamAudioMaterial>();
            AssetDatabase.CreateAsset(material, folder + "/RoomSurfaces.asset");
        }
        material.lowFreqAbsorption = 0.10f;
        material.midFreqAbsorption = 0.15f;
        material.highFreqAbsorption = 0.25f;
        material.scattering = 0.6f;
        material.lowFreqTransmission = 0;
        material.midFreqTransmission = 0;
        material.highFreqTransmission = 0;
        EditorUtility.SetDirty(material);

        var root = GameObject.Find("Cube Room (10m Interior)");
        if (root == null) throw new InvalidOperationException("Room root missing.");
        var geometry = root.GetComponent<SteamAudioGeometry>();
        if (geometry == null) geometry = root.AddComponent<SteamAudioGeometry>();
        geometry.exportAllChildren = true;
        geometry.material = material;
        EditorUtility.SetDirty(geometry);

        var mesh = UnityEngine.Object.FindAnyObjectByType<SteamAudioStaticMesh>();
        if (mesh == null) mesh = new GameObject("Steam Audio Static Mesh").AddComponent<SteamAudioStaticMesh>();
        var data = AssetDatabase.LoadAssetAtPath<SerializedData>(folder + "/CubeRoomGeometry.asset");
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<SerializedData>();
            AssetDatabase.CreateAsset(data, folder + "/CubeRoomGeometry.asset");
        }
        mesh.asset = data;
        mesh.sceneNameWhenExported = scene.name;
        SteamAudioManager.ExportScene(scene, false);
        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(mesh);

        var settings = SteamAudioSettings.Singleton;
        settings.realTimeRays = 4096;
        settings.realTimeBounces = 16;
        settings.realTimeDuration = 1.5f;
        settings.realTimeAmbisonicOrder = 1;
        settings.realTimeMaxSources = 16;
        settings.simulationUpdateInterval = 0.1f;
        settings.reflectionEffectType = ReflectionEffectType.Convolution;
        EditorUtility.SetDirty(settings);

        var controller = UnityEngine.Object.FindAnyObjectByType<BouncePlaytest>();
        controller.reverb = true;
        controller.reverbLevel = 0.65f;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Exported room geometry: " + data.data.Length + " bytes; realtime reflections 1.5s, 16 bounces; M toggles reverb, Y plays a single knock.";
    }
}
