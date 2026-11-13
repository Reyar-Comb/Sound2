using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class CreateCubeRoom
{
    public static string Main()
    {
        const string scenePath = "Assets/Scenes/CubeRoom.unity";
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        if (System.IO.File.Exists(scenePath)) throw new InvalidOperationException("CubeRoom already exists.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the current scene before generating the room.");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/Materials/CubeRoom"))
            AssetDatabase.CreateFolder("Assets/Materials", "CubeRoom");

        Material floor = Material("Floor", new Color(0.19f, 0.23f, 0.28f), shader);
        Material ceiling = Material("Ceiling", new Color(0.78f, 0.81f, 0.83f), shader);
        Material front = Material("Front", new Color(0.35f, 0.54f, 0.59f), shader);
        Material back = Material("Back", new Color(0.48f, 0.52f, 0.58f), shader);
        Material left = Material("Left", new Color(0.57f, 0.42f, 0.34f), shader);
        Material right = Material("Right", new Color(0.40f, 0.48f, 0.62f), shader);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var room = new GameObject("Cube Room (10m Interior)");
        // Inner faces span x/z = -5..5 and y = 0..10. Thickness is outside this volume.
        Surface("Floor", new Vector3(0, -0.15f, 0), new Vector3(10.6f, 0.3f, 10.6f), floor, room.transform);
        Surface("Ceiling", new Vector3(0, 10.15f, 0), new Vector3(10.6f, 0.3f, 10.6f), ceiling, room.transform);
        Surface("Wall Front (+Z)", new Vector3(0, 5, 5.15f), new Vector3(10.6f, 10, 0.3f), front, room.transform);
        Surface("Wall Back (-Z)", new Vector3(0, 5, -5.15f), new Vector3(10.6f, 10, 0.3f), back, room.transform);
        Surface("Wall Left (-X)", new Vector3(-5.15f, 5, 0), new Vector3(0.3f, 10, 10.6f), left, room.transform);
        Surface("Wall Right (+X)", new Vector3(5.15f, 5, 0), new Vector3(0.3f, 10, 10.6f), right, room.transform);

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0, 1.6f, -3.5f);
        cameraObject.transform.rotation = Quaternion.Euler(-8, 0, 0);
        var camera = cameraObject.GetComponent<Camera>();
        camera.fieldOfView = 75;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 50;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.10f, 0.13f);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.64f);
        RenderSettings.ambientIntensity = 1;
        RenderSettings.skybox = null;
        var lighting = new GameObject("Room Lighting");
        var lamp = new GameObject("Ceiling Point Light", typeof(Light));
        lamp.transform.SetParent(lighting.transform);
        lamp.transform.position = new Vector3(0, 8.5f, 0);
        var light = lamp.GetComponent<Light>();
        light.type = LightType.Point;
        light.range = 22;
        light.intensity = 3;
        light.color = new Color(1f, 0.94f, 0.86f);
        light.shadows = LightShadows.None;

        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene, scenePath))
            throw new InvalidOperationException("Could not save the room scene.");
        Selection.activeGameObject = room;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(0, 3, 0), Quaternion.Euler(15, 0, 0), 7);
        return "Saved " + scenePath + "; interior 10 x 10 x 10m; six BoxColliders; one camera and AudioListener.";
    }

    private static Material Material(string name, Color color, Shader shader)
    {
        string path = "Assets/Materials/CubeRoom/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader) { name = "CubeRoom " + name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void Surface(string name, Vector3 position, Vector3 size, Material material, Transform parent)
    {
        var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        surface.name = name;
        surface.transform.SetParent(parent);
        surface.transform.position = position;
        surface.transform.localScale = size;
        surface.GetComponent<Renderer>().sharedMaterial = material;
        surface.isStatic = true;
    }
}
