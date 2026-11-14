using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ConfigureSchoolProps
{
    const string Folder = "Assets/Models/SchoolProps";
    static Material wood, steel, dark, ivory, green, chalk, pink, blue;
    static GameObject root;
    static readonly List<Mesh> temporary = new();
    static readonly List<string> created = new();

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring props.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh(); created.Clear();
        wood = Existing("Wood"); steel = Existing("Steel"); dark = Existing("Charcoal"); ivory = Existing("Enamel");
        green = Mat("BlackboardGreen", new Color(.055f, .16f, .125f));
        chalk = Mat("ChalkWhite", new Color(.86f, .85f, .74f));
        pink = Mat("ChalkPink", new Color(.69f, .39f, .37f));
        blue = Existing("Blue");
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            Build(stage, "SchoolBlackboard", Blackboard, PropSurface.Wall);
            Build(stage, "SchoolDesk", Desk, PropSurface.Floor);
            Build(stage, "SchoolChair", Chair, PropSurface.Floor);
            Build(stage, "SchoolChalkBox", ChalkBox, PropSurface.Floor);
            Build(stage, "SchoolSpeaker", Speaker, PropSurface.Wall);
        }
        finally { EditorSceneManager.ClosePreviewScene(stage); }
        AssetDatabase.SaveAssets(); return new { created };
    }
    static Material Existing(string name)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/HouseholdProps/" + name + ".mat");
        if (material == null) throw new InvalidOperationException("Missing shared material: " + name);
        return material;
    }
    static Material Mat(string name, Color color)
    {
        var path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
        material.SetFloat("_Smoothness", .2f); AssetDatabase.CreateAsset(material, path); return material;
    }
    static void Build(Scene scene, string name, Action model, PropSurface surface)
    {
        string path = "Assets/Prefabs/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            model(); Bake(name);
            var prop = root.AddComponent<LevelProp>(); prop.surface = surface;
            prop.surfaceAnchor = Vector3.zero; prop.surfaceOffset = .01f;
            prop.gameplayMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            PrefabUtility.SaveAsPrefabAsset(root, path); created.Add(path);
        }
        finally
        {
            Object.DestroyImmediate(root); root = null;
            foreach (var mesh in temporary) if (mesh != null && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);
            temporary.Clear();
        }
    }
    static GameObject Part(string name, PrimitiveType type, Material material, Vector3 position, Vector3 scale, Quaternion rotation = default)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        go.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
        go.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
    }
    static void Box(string name, Material material, Vector3 p, Vector3 size) => Part(name, PrimitiveType.Cube, material, p, size);
    static void Rod(string name, Material material, Vector3 a, Vector3 b, float width) =>
        Part(name, PrimitiveType.Cylinder, material, (a + b) * .5f, new Vector3(width, (b - a).magnitude * .5f, width), Quaternion.FromToRotation(Vector3.up, b - a));
    static void Disc(string name, Material material, Vector3 p, float diameter, float depth) =>
        Part(name, PrimitiveType.Cylinder, material, p, new Vector3(diameter, depth * .5f, diameter), Quaternion.Euler(90, 0, 0));
    static void Collision(Vector3 p, Vector3 size) { var c = root.AddComponent<BoxCollider>(); c.center = p; c.size = size; }
    static void Bake(string name)
    {
        var parts = root.GetComponentsInChildren<MeshFilter>();
        var groups = parts.GroupBy(p => p.GetComponent<Renderer>().sharedMaterial).ToArray();
        var combinedParts = new List<CombineInstance>();
        foreach (var group in groups)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; temporary.Add(mesh);
            mesh.CombineMeshes(group.Select(p => new CombineInstance { mesh = p.sharedMesh, transform = root.transform.worldToLocalMatrix * p.transform.localToWorldMatrix }).ToArray());
            combinedParts.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.identity });
        }
        var combined = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        combined.CombineMeshes(combinedParts.ToArray(), false);
        combined = PropMeshAssets.Store(Folder, name, combined);
        foreach (var part in parts) Object.DestroyImmediate(part.gameObject);
        var model = new GameObject("Model", typeof(MeshFilter), typeof(MeshRenderer)); model.transform.SetParent(root.transform, false);
        model.GetComponent<MeshFilter>().sharedMesh = combined; model.GetComponent<Renderer>().sharedMaterials = groups.Select(g => g.Key).ToArray();
    }
    static void Blackboard()
    {
        Box("Board backing", wood, new Vector3(0, 0, .025f), new Vector3(2.4f, 1.2f, .05f));
        Box("Writing surface", green, new Vector3(0, 0, .055f), new Vector3(2.29f, 1.09f, .014f));
        foreach (int s in new[] { -1, 1 })
        {
            Box("Vertical frame", wood, new Vector3(s * 1.18f, 0, .065f), new Vector3(.04f, 1.2f, .06f));
            Box("Horizontal frame", wood, new Vector3(0, s * .58f, .065f), new Vector3(2.4f, .04f, .06f));
        }
        Box("Chalk tray", steel, new Vector3(0, -.62f, .12f), new Vector3(2.4f, .025f, .24f));
        Box("Tray lip", steel, new Vector3(0, -.6f, .235f), new Vector3(2.4f, .05f, .015f));
        Box("Eraser felt", dark, new Vector3(.78f, -.588f, .14f), new Vector3(.16f, .04f, .07f));
        Box("Eraser grip", wood, new Vector3(.78f, -.563f, .14f), new Vector3(.16f, .02f, .07f));
        Rod("Chalk on tray", chalk, new Vector3(-.7f, -.593f, .14f), new Vector3(-.61f, -.593f, .14f), .012f);
        Collision(new Vector3(0, 0, .05f), new Vector3(2.4f, 1.2f, .1f));
        Collision(new Vector3(0, -.607f, .12f), new Vector3(2.4f, .05f, .24f));
    }
    static void Desk()
    {
        Box("Desktop", wood, new Vector3(0, .735f, 0), new Vector3(.66f, .045f, .46f));
        Box("Under desk shelf", steel, new Vector3(0, .55f, .025f), new Vector3(.56f, .018f, .35f));
        Box("Shelf back", steel, new Vector3(0, .625f, .192f), new Vector3(.56f, .15f, .018f));
        foreach (int x in new[] { -1, 1 })
        {
            Box("Shelf side", steel, new Vector3(x * .272f, .625f, .025f), new Vector3(.016f, .15f, .35f));
            foreach (int z in new[] { -1, 1 })
            {
                var foot = new Vector3(x * .275f, .015f, z * .175f);
                Rod("Tubular leg", steel, foot, new Vector3(x * .25f, .71f, z * .155f), .028f);
                Box("Foot cap", dark, foot, new Vector3(.038f, .03f, .038f));
                Collision(new Vector3(x * .265f, .355f, z * .165f), new Vector3(.05f, .71f, .05f));
            }
            Rod("Side brace", steel, new Vector3(x * .27f, .24f, -.17f), new Vector3(x * .27f, .24f, .17f), .022f);
        }
        Rod("Rear brace", steel, new Vector3(-.27f, .28f, .17f), new Vector3(.27f, .28f, .17f), .022f);
        Box("Bag hook", dark, new Vector3(.3f, .65f, .02f), new Vector3(.055f, .018f, .025f));
        Box("Hook tip", dark, new Vector3(.323f, .66f, .02f), new Vector3(.012f, .03f, .025f));
        Box("Pencil groove", dark, new Vector3(0, .758f, .16f), new Vector3(.32f, .002f, .012f));
        Collision(new Vector3(0, .735f, 0), new Vector3(.66f, .045f, .46f));
        Collision(new Vector3(0, .62f, .025f), new Vector3(.58f, .16f, .36f));
    }
    static void Chair()
    {
        Box("Wood seat", wood, new Vector3(0, .435f, 0), new Vector3(.38f, .035f, .38f));
        Box("Wood backrest", wood, new Vector3(0, .695f, .17f), new Vector3(.38f, .18f, .028f));
        foreach (int x in new[] { -1, 1 })
        {
            foreach (int z in new[] { -1, 1 })
            {
                var foot = new Vector3(x * .16f, .015f, z * .16f);
                Rod("Tubular leg", steel, foot, new Vector3(x * .145f, .42f, z * .145f), .025f);
                Box("Foot cap", dark, foot, new Vector3(.035f, .03f, .035f));
                Collision(new Vector3(x * .153f, .21f, z * .153f), new Vector3(.04f, .42f, .04f));
            }
            Rod("Back upright", steel, new Vector3(x * .145f, .4f, .15f), new Vector3(x * .145f, .77f, .195f), .025f);
            Rod("Side brace", steel, new Vector3(x * .155f, .22f, -.15f), new Vector3(x * .155f, .22f, .15f), .02f);
            Disc("Backrest rivet", steel, new Vector3(x * .145f, .695f, .153f), .014f, .004f);
        }
        Rod("Front brace", steel, new Vector3(-.15f, .22f, -.15f), new Vector3(.15f, .22f, -.15f), .02f);
        Collision(new Vector3(0, .435f, 0), new Vector3(.38f, .035f, .38f));
        Collision(new Vector3(0, .695f, .18f), new Vector3(.38f, .18f, .05f));
    }
    static void ChalkBox()
    {
        Box("Box bottom", ivory, new Vector3(0, .006f, 0), new Vector3(.20f, .012f, .13f));
        foreach (int s in new[] { -1, 1 })
        {
            Box("Box side", blue, new Vector3(s * .096f, .029f, 0), new Vector3(.008f, .058f, .13f));
            Box("Box end", blue, new Vector3(0, .029f, s * .061f), new Vector3(.20f, .058f, .008f));
        }
        Box("Label", ivory, new Vector3(0, .03f, -.066f), new Vector3(.10f, .029f, .002f));
        for (int i = 0; i < 10; i++)
        {
            float x = -.08f + i * .0175f;
            Rod("Chalk stick", i < 7 ? chalk : pink, new Vector3(x, .022f, -.043f), new Vector3(x, .022f, .039f + (i % 3) * .004f), .013f);
        }
        for (int i = 0; i < 5; i++)
        {
            float x = -.07f + i * .03f;
            Rod("Upper chalk", chalk, new Vector3(x, .036f, -.044f), new Vector3(x + .008f, .036f, .04f), .013f);
        }
        Collision(new Vector3(0, .029f, 0), new Vector3(.20f, .058f, .13f));
    }
    static void Speaker()
    {
        Box("Wall bracket", steel, new Vector3(0, 0, .015f), new Vector3(.26f, .34f, .03f));
        Box("Speaker cabinet", wood, new Vector3(0, 0, .12f), new Vector3(.34f, .44f, .19f));
        Box("Front inset", dark, new Vector3(0, 0, .218f), new Vector3(.29f, .39f, .012f));
        Disc("Woofer rim", steel, new Vector3(0, -.065f, .229f), .225f, .012f);
        Disc("Woofer cone", dark, new Vector3(0, -.065f, .24f), .199f, .014f);
        Disc("Woofer cap", ivory, new Vector3(0, -.065f, .25f), .065f, .012f);
        Disc("Tweeter", steel, new Vector3(0, .12f, .23f), .075f, .014f);
        Disc("Tweeter cap", dark, new Vector3(0, .12f, .24f), .054f, .014f);
        for (int i = 0; i < 15; i++) Box("Protective grille", dark, new Vector3(0, -.175f + i * .025f, .264f), new Vector3(.28f, .007f, .012f));
        foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
            Disc("Corner screw", steel, new Vector3(x * .153f, y * .202f, .22f), .012f, .006f);
        Collision(new Vector3(0, 0, .135f), new Vector3(.34f, .44f, .27f));
    }
}
