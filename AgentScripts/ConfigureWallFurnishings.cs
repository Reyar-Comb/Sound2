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

public static class ConfigureWallFurnishings
{
    const string Folder = "Assets/Models/WallFurnishings";
    static GameObject root;
    static Material walnut, oak, brass, ivory, teal, terra, leaf, petal, silver, dark, stone;
    static readonly List<Mesh> temporary = new();
    static readonly List<string> created = new();

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring props.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh(); created.Clear();
        walnut = Mat("Walnut", new Color(.23f, .105f, .055f));
        oak = Mat("Wicker", new Color(.61f, .37f, .16f));
        brass = Mat("Brass", new Color(.72f, .47f, .18f), .65f);
        ivory = Mat("Ivory", new Color(.87f, .82f, .65f));
        teal = Mat("Teal", new Color(.10f, .32f, .31f));
        terra = Mat("Terracotta", new Color(.62f, .24f, .13f));
        leaf = Mat("Leaves", new Color(.16f, .34f, .09f));
        petal = Mat("Petals", new Color(.78f, .24f, .31f));
        silver = Mat("MirrorSilver", new Color(.67f, .83f, .87f), .95f);
        dark = Mat("Charcoal", new Color(.065f, .075f, .075f));
        stone = Mat("CounterStone", new Color(.16f, .20f, .20f), .2f);
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            Build(stage, "WallMural", Mural, PropSurface.Wall);
            Build(stage, "WallMirror", Mirror, PropSurface.Wall);
            Build(stage, "WallFlowerBasket", Basket, PropSurface.Wall);
            Build(stage, "WallCabinet", Cabinet, PropSurface.Wall);
            Build(stage, "BarCounter", Bar, PropSurface.Floor, false);
        }
        finally { EditorSceneManager.ClosePreviewScene(stage); }
        AssetDatabase.SaveAssets();
        return new { created };
    }

    static Material Mat(string name, Color color, float metallic = 0)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
        material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", metallic > .5f ? .85f : .3f);
        AssetDatabase.CreateAsset(material, path); return material;
    }

    static void Build(Scene stage, string name, Action model, PropSurface surface, bool bake = true)
    {
        string path = "Assets/Prefabs/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, stage);
        try
        {
            model(); if (bake) Bake(name);
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

    static GameObject Part(string name, PrimitiveType type, Material mat, Vector3 p, Vector3 scale, Quaternion rotation = default)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(root.transform, false);
        go.transform.localPosition = p; go.transform.localScale = scale;
        go.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
        go.GetComponent<Renderer>().sharedMaterial = mat; Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }
    static GameObject Box(string name, Material mat, Vector3 p, Vector3 size) => Part(name, PrimitiveType.Cube, mat, p, size);
    static GameObject Rod(string name, Material mat, Vector3 a, Vector3 b, float diameter) =>
        Part(name, PrimitiveType.Cylinder, mat, (a + b) * .5f, new Vector3(diameter, (b - a).magnitude * .5f, diameter), Quaternion.FromToRotation(Vector3.up, b - a));
    static void Disc(string name, Material mat, Vector3 p, float rx, float ry, float depth) =>
        Part(name, PrimitiveType.Cylinder, mat, p, new Vector3(rx * 2, depth * .5f, ry * 2), Quaternion.Euler(90, 0, 0));
    static void Collision(Vector3 p, Vector3 size) { var c = root.AddComponent<BoxCollider>(); c.center = p; c.size = size; }
    static void MeshPart(string name, Material mat, List<Vector3> vertices, List<int> indices)
    {
        var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); temporary.Add(mesh);
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(root.transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<Renderer>().sharedMaterial = mat;
    }
    static void Bake(string name)
    {
        var parts = root.GetComponentsInChildren<MeshFilter>();
        var groups = parts.GroupBy(p => p.GetComponent<Renderer>().sharedMaterial).ToArray();
        var submeshes = new List<CombineInstance>();
        foreach (var group in groups)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; temporary.Add(mesh);
            mesh.CombineMeshes(group.Select(p => new CombineInstance { mesh = p.sharedMesh, transform = root.transform.worldToLocalMatrix * p.transform.localToWorldMatrix }).ToArray());
            submeshes.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.identity });
        }
        var combined = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        combined.CombineMeshes(submeshes.ToArray(), false);
        combined = PropMeshAssets.Store(Folder, name, combined);
        foreach (var part in parts) Object.DestroyImmediate(part.gameObject);
        var model = new GameObject("Model", typeof(MeshFilter), typeof(MeshRenderer)); model.transform.SetParent(root.transform, false);
        model.GetComponent<MeshFilter>().sharedMesh = combined;
        model.GetComponent<Renderer>().sharedMaterials = groups.Select(g => g.Key).ToArray();
    }

    static Material PaintingMaterial()
    {
        const string path = Folder + "/Landscape.png";
        if (!File.Exists(path))
        {
            var texture = new Texture2D(512, 320, TextureFormat.RGB24, false);
            for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
            {
                float u = x / 511f, v = y / 319f;
                Color color = new Color(.84f, .76f, .58f);
                if (Vector2.Distance(new Vector2(u, v), new Vector2(.24f, .74f)) < .115f) color = new Color(.83f, .48f, .20f);
                if (v < .66f - Mathf.Abs(u - .29f) * .95f) color = new Color(.48f, .39f, .33f);
                if (v < .78f - Mathf.Abs(u - .65f) * 1.1f) color = new Color(.20f, .37f, .35f);
                if (v < .38f - Mathf.Abs(u - .18f) * .20f) color = new Color(.14f, .25f, .24f);
                if (v < .22f) color = new Color(.37f, .49f, .46f);
                if (v < .19f && y % 13 == 0 && u > .16f + v && u < .82f - v) color = new Color(.67f, .68f, .53f);
                texture.SetPixel(x, y, color);
            }
            texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
        }
        var material = Mat("PaintedCanvas", Color.white);
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path); EditorUtility.SetDirty(material);
        return material;
    }
    static void Mural()
    {
        Box("Canvas backing", ivory, new Vector3(0, 0, .025f), new Vector3(1.6f, 1.05f, .05f));
        foreach (int side in new[] { -1, 1 })
        {
            Box("Frame vertical", walnut, new Vector3(side * .805f, 0, .045f), new Vector3(.085f, 1.14f, .09f));
            Box("Frame horizontal", walnut, new Vector3(0, side * .525f, .045f), new Vector3(1.53f, .085f, .09f));
            Box("Brass vertical inlay", brass, new Vector3(side * .763f, 0, .09f), new Vector3(.012f, 1.02f, .012f));
            Box("Brass horizontal inlay", brass, new Vector3(0, side * .482f, .09f), new Vector3(1.52f, .012f, .012f));
        }
        Part("Flat painted canvas", PrimitiveType.Quad, PaintingMaterial(), new Vector3(0, 0, .051f), new Vector3(1.52f, .96f, 1), Quaternion.Euler(0, 180, 0));
        Collision(new Vector3(0, 0, .048f), new Vector3(1.695f, 1.14f, .096f));
    }
    static void Mirror()
    {
        Disc("Oval back", walnut, new Vector3(0, 0, .018f), .46f, .66f, .036f);
        Disc("Silver glass", silver, new Vector3(0, 0, .043f), .412f, .612f, .014f);
        var v = new List<Vector3>(); var t = new List<int>();
        const int segments = 64;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
            var profile = new[] { new Vector3(.415f, .615f, .036f), new Vector3(.415f, .615f, .065f), new Vector3(.443f, .643f, .09f), new Vector3(.475f, .675f, .06f), new Vector3(.475f, .675f, .012f) };
            for (int j = 0; j < profile.Length; j++)
            {
                var p = profile[j]; var q = profile[(j + 1) % profile.Length]; int n = v.Count;
                Vector3 P(Vector3 r, float angle) => new Vector3(r.x * Mathf.Cos(angle), r.y * Mathf.Sin(angle), r.z);
                v.AddRange(new[] { P(p, a), P(q, a), P(q, b), P(p, b) }); t.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
            }
        }
        MeshPart("Bevelled brass frame", brass, v, t);
        Disc("Crest", brass, new Vector3(0, .69f, .045f), .07f, .045f, .025f);
        Collision(new Vector3(0, 0, .045f), new Vector3(.95f, 1.47f, .09f));
    }
    static void Basket()
    {
        Box("Wall bracket", brass, new Vector3(0, .22f, .02f), new Vector3(.09f, .48f, .04f));
        Rod("Hanger", brass, new Vector3(0, .45f, .025f), new Vector3(0, .42f, .19f), .022f);
        Rod("Left strap", oak, new Vector3(-.31f, -.05f, .19f), new Vector3(0, .42f, .19f), .024f);
        Rod("Right strap", oak, new Vector3(.31f, -.05f, .19f), new Vector3(0, .42f, .19f), .024f);
        Box("Basket flat back", oak, new Vector3(0, -.19f, .025f), new Vector3(.59f, .29f, .05f));
        for (int row = 0; row < 9; row++)
        {
            float f = row / 8f, rx = Mathf.Lerp(.22f, .34f, f), rz = Mathf.Lerp(.12f, .24f, f), y = Mathf.Lerp(-.35f, -.03f, f);
            for (int i = 0; i < 18; i++)
            {
                float a = i * Mathf.PI / 18, b = (i + 1) * Mathf.PI / 18;
                Rod("Wicker weave", row % 2 == 0 ? oak : walnut, new Vector3(rx * Mathf.Cos(a), y, .04f + rz * Mathf.Sin(a)), new Vector3(rx * Mathf.Cos(b), y, .04f + rz * Mathf.Sin(b)), row == 8 ? .03f : .014f);
            }
        }
        for (int i = 0; i <= 12; i++)
        {
            float a = i * Mathf.PI / 12;
            Rod("Basket rib", oak, new Vector3(.22f * Mathf.Cos(a), -.35f, .04f + .12f * Mathf.Sin(a)), new Vector3(.34f * Mathf.Cos(a), -.03f, .04f + .24f * Mathf.Sin(a)), .016f);
        }
        Part("Soil", PrimitiveType.Sphere, dark, new Vector3(0, -.05f, .12f), new Vector3(.57f, .08f, .22f));
        for (int i = 0; i < 7; i++)
        {
            float x = -.25f + i * .083f, y = .11f + (i * 3 % 5) * .045f, z = .17f + (i % 2) * .06f;
            var center = new Vector3(x, y, z);
            Rod("Flower stem", leaf, new Vector3(x * .7f, -.08f, .15f), center, .01f);
            for (int side = -1; side <= 1; side += 2)
                Part("Leaf", PrimitiveType.Sphere, leaf, new Vector3(x + side * .045f, y * .35f, z), new Vector3(.11f, .035f, .05f), Quaternion.Euler(0, 0, side * 25));
            for (int p = 0; p < 5; p++)
            {
                float a = p * Mathf.PI * 2 / 5;
                Part("Petal", PrimitiveType.Sphere, i % 2 == 0 ? petal : ivory, center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * .037f,
                    new Vector3(.06f, .045f, .025f), Quaternion.Euler(0, 0, a * Mathf.Rad2Deg));
            }
            Part("Flower heart", PrimitiveType.Sphere, brass, center + Vector3.forward * .018f, Vector3.one * .028f);
        }
        Collision(new Vector3(0, -.19f, .14f), new Vector3(.68f, .34f, .28f));
        Collision(new Vector3(0, .22f, .02f), new Vector3(.09f, .48f, .04f));
    }
    static void Cabinet()
    {
        Box("Back", walnut, new Vector3(0, 0, .025f), new Vector3(1.16f, .90f, .05f));
        foreach (int side in new[] { -1, 1 })
        {
            Box("Side", walnut, new Vector3(side * .565f, 0, .20f), new Vector3(.07f, .90f, .40f));
            Box("Crown and base", walnut, new Vector3(0, side * .45f, .21f), new Vector3(1.24f, .075f, .43f));
            float x = side * .287f;
            Box("Door", oak, new Vector3(x, 0, .408f), new Vector3(.554f, .83f, .045f));
            Box("Recessed panel", walnut, new Vector3(x, 0, .433f), new Vector3(.43f, .68f, .014f));
            Box("Panel center", oak, new Vector3(x, 0, .443f), new Vector3(.38f, .63f, .015f));
            Rod("Handle", brass, new Vector3(side * .07f, -.10f, .49f), new Vector3(side * .07f, .10f, .49f), .018f);
            for (int end = -1; end <= 1; end += 2)
                Rod("Handle mount", brass, new Vector3(side * .07f, end * .10f, .43f), new Vector3(side * .07f, end * .10f, .49f), .018f);
        }
        Box("Interior shelf", walnut, new Vector3(0, 0, .19f), new Vector3(1.1f, .035f, .34f));
        Collision(new Vector3(0, 0, .22f), new Vector3(1.24f, .975f, .44f));
    }
    static void Bar()
    {
        var bindings = new List<ResizableBar.Part>();
        void Span(string name, Material mat, Vector3 position, float height, float depth, float ratio = 1, float inset = 0, float xRatio = 0)
        {
            var part = Box(name, mat, position, new Vector3(1, height, depth)); part.AddComponent<BoxCollider>();
            bindings.Add(new ResizableBar.Part { target = part.transform, positionRatio = xRatio, lengthRatio = ratio, lengthOffset = inset });
        }
        Span("Stone countertop", stone, new Vector3(0, 1.07f, 0), .10f, .82f, 1, .16f);
        Span("Countertop brass edge", brass, new Vector3(0, 1.022f, .405f), .018f, .016f, 1, .16f);
        Span("Lower plinth", dark, new Vector3(0, .06f, -.04f), .12f, .62f);
        Span("Front cabinet", walnut, new Vector3(0, .57f, .27f), .90f, .075f, 1, -.10f);
        Span("Rear work shelf", oak, new Vector3(0, .54f, -.08f), .055f, .54f, 1, -.12f);
        Span("Bottom shelf", oak, new Vector3(0, .15f, -.08f), .055f, .54f, 1, -.12f);
        for (int i = -1; i <= 1; i++)
        {
            Span("Front inset " + i, oak, new Vector3(0, .57f, .312f), .68f, .018f, 1f / 3, -.10f, i / 3f);
            Span("Inset center " + i, walnut, new Vector3(0, .57f, .325f), .58f, .014f, 1f / 3, -.17f, i / 3f);
        }
        foreach (int side in new[] { -1, 1 })
        {
            var end = new GameObject(side < 0 ? "Left end" : "Right end"); end.transform.SetParent(root.transform, false);
            var panel = Box("End panel", walnut, new Vector3(0, .56f, -.04f), new Vector3(.065f, .90f, .62f));
            panel.AddComponent<BoxCollider>(); panel.transform.SetParent(end.transform, false);
            var inset = Box("End inset", oak, new Vector3(side * .037f, .56f, -.04f), new Vector3(.016f, .70f, .46f)); inset.transform.SetParent(end.transform, false);
            bindings.Add(new ResizableBar.Part { target = end.transform, positionRatio = side * .5f, positionOffset = -side * .033f });
            var support = Rod("Footrail bracket", brass, new Vector3(0, .23f, .30f), new Vector3(0, .23f, .51f), .028f);
            bindings.Add(new ResizableBar.Part { target = support.transform, positionRatio = side * .5f, positionOffset = -side * .17f });
        }
        var rail = Rod("Brass footrail", brass, new Vector3(-.5f, .23f, .51f), new Vector3(.5f, .23f, .51f), .035f);
        var mesh = Object.Instantiate(rail.GetComponent<MeshFilter>().sharedMesh);
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = rail.transform.localRotation * Vector3.Scale(vertices[i], rail.transform.localScale);
        mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        rail.GetComponent<MeshFilter>().sharedMesh = PropMeshAssets.Store(Folder, "BarCounter", mesh);
        rail.transform.localRotation = Quaternion.identity; rail.transform.localScale = Vector3.one;
        var railCollider = rail.AddComponent<BoxCollider>(); railCollider.size = new Vector3(1, .035f, .035f);
        bindings.Add(new ResizableBar.Part { target = rail.transform, lengthRatio = 1, lengthOffset = -.18f });
        root.AddComponent<ResizableBar>().SetParts(bindings.ToArray());
    }
}
