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

public static class ConfigureRestaurantProps
{
    const string Folder = "Assets/Models/RestaurantProps";
    static GameObject root;
    static Material wood, burgundy, gold, dark;
    static readonly List<Mesh> temporary = new();
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            MakeTable(scene); MakeStage(scene); AssetDatabase.SaveAssets();
            return new { table = "Assets/Prefabs/DrapedRingTable.prefab", stage = "Assets/Prefabs/RestaurantStage.prefab", stageDeck = "14m wide, 10.5m deep, 0.6m high", elevatorClearance = "center 6m x 7m; open overhead" };
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root); root = null;
            foreach (var mesh in temporary) if (mesh != null && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);
            temporary.Clear(); EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static Material Mat(string name, Color color, float metallic = 0)
    {
        var path = Folder + "/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
        material.SetFloat("_Smoothness", metallic > 0 ? .35f : .18f); material.SetFloat("_Metallic", metallic);
        AssetDatabase.CreateAsset(material, path); return material;
    }
    static void Metadata()
    {
        var prop = root.AddComponent<LevelProp>(); prop.surface = PropSurface.Floor; prop.surfaceAnchor = Vector3.zero; prop.surfaceOffset = .01f;
        prop.gameplayMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
    }
    static void MakeTable(Scene scene)
    {
        const string path = "Assets/Prefabs/DrapedRingTable.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        root = new GameObject("DrapedRingTable"); SceneManager.MoveGameObjectToScene(root, scene);
        var table = root.AddComponent<DrapedRingTable>(); table.Rebuild();
        root.GetComponent<Renderer>().sharedMaterial = Mat("WarmIvoryCloth", new Color(.72f, .66f, .52f));
        var visual = Object.Instantiate(root.GetComponent<MeshFilter>().sharedMesh);
        var collision = Object.Instantiate(root.GetComponent<MeshCollider>().sharedMesh);
        visual = PropMeshAssets.Store(Folder, "DrapedRingTable", visual);
        collision.name = "Collision"; AssetDatabase.AddObjectToAsset(collision, visual);
        table.UseStoredMeshes(visual, collision); Metadata(); PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root); root = null;
    }
    static void Box(string name, Material material, Vector3 position, Vector3 size, bool collision = true)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name; part.transform.SetParent(root.transform, false);
        part.transform.localPosition = position; part.transform.localScale = size; part.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        if (collision) { var box = root.AddComponent<BoxCollider>(); box.center = position; box.size = size; }
    }
    static List<Vector2> Outline()
    {
        var outline = new List<Vector2>();
        for (int i = 0; i <= 96; i++) { float a = i * Mathf.PI / 96; outline.Add(new Vector2(7 * Mathf.Cos(a), -6 * Mathf.Sin(a))); }
        outline.Add(new Vector2(-7, 4.5f)); outline.Add(new Vector2(7, 4.5f)); return outline;
    }
    static Mesh DeckMesh(List<Vector2> outline, float bottom, float top, bool includeTop, bool includeBottom)
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        { int first = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2); }
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % outline.Count];
            var lowA = new Vector3(a.x, bottom, a.y); var lowB = new Vector3(b.x, bottom, b.y);
            var highA = new Vector3(a.x, top, a.y); var highB = new Vector3(b.x, top, b.y);
            if (top > bottom) { Tri(lowA, lowB, highA); Tri(lowB, highB, highA); }
            if (includeTop) Tri(new Vector3(0, top, 0), highA, highB);
            if (includeBottom) Tri(new Vector3(0, bottom, 0), lowB, lowA);
        }
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); temporary.Add(mesh); return mesh;
    }
    static void MeshPart(string name, Mesh mesh, Material material)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(root.transform, false);
        part.GetComponent<MeshFilter>().sharedMesh = mesh; part.GetComponent<Renderer>().sharedMaterial = material;
    }
    static void MakeStage(Scene scene)
    {
        const string path = "Assets/Prefabs/RestaurantStage.prefab";
        wood = Mat("StageWood", new Color(.31f, .17f, .09f)); burgundy = Mat("StageBurgundy", new Color(.23f, .055f, .065f));
        gold = Mat("StageBrass", new Color(.55f, .37f, .16f), .45f); dark = Mat("StageCharcoal", new Color(.07f, .065f, .06f));
        root = new GameObject("RestaurantStage"); SceneManager.MoveGameObjectToScene(root, scene);
        var outline = Outline();
        MeshPart("Platform fascia", DeckMesh(outline, 0, .6f, false, true), burgundy);
        MeshPart("Open central deck", DeckMesh(outline, .6f, .6f, true, false), wood);
        var expanded = outline.Select(v => v * 1.0015f).ToList();
        MeshPart("Lower brass band", DeckMesh(expanded, .07f, .095f, false, false), gold);
        MeshPart("Upper brass band", DeckMesh(expanded, .515f, .545f, false, false), gold);
        var deckCollider = root.AddComponent<MeshCollider>();
        var collisionMesh = DeckMesh(outline, 0, .6f, true, true); collisionMesh.name = "StageDeckCollision";
        deckCollider.sharedMesh = collisionMesh;
        Box("Backdrop", burgundy, new Vector3(0, 3.05f, 4.5f), new Vector3(14, 4.9f, .36f));
        Box("Backdrop inset", dark, new Vector3(0, 3.02f, 4.305f), new Vector3(12.8f, 4.05f, .035f), false);
        Box("Inset panel", burgundy, new Vector3(0, 3.02f, 4.28f), new Vector3(12.58f, 3.83f, .025f), false);
        Box("Upper cornice", wood, new Vector3(0, 5.52f, 4.47f), new Vector3(14.3f, .16f, .62f));
        Box("Cornice brass", gold, new Vector3(0, 5.425f, 4.22f), new Vector3(14.18f, .035f, .05f), false);
        foreach (int side in new[] { -1, 1 })
        {
            Box("Side return", burgundy, new Vector3(side * 6.88f, 3.05f, 3.8f), new Vector3(.24f, 4.9f, 1.4f));
            Box("Side cap", wood, new Vector3(side * 6.88f, 5.52f, 3.75f), new Vector3(.42f, .16f, 1.6f));
            Box("Backdrop pilaster", wood, new Vector3(side * 6.6f, 3.05f, 4.25f), new Vector3(.16f, 4.9f, .22f));
            Box("Pilaster inlay", gold, new Vector3(side * 6.6f, 3.05f, 4.129f), new Vector3(.035f, 4.65f, .025f), false);
            for (int step = 0; step < 3; step++)
            {
                float front = -6.05f + step * .72f, back = -2.8f, rise = (step + 1) * .2f;
                Box("Stair " + side + " " + step, wood, new Vector3(side * 5.05f, rise * .5f, (front + back) * .5f), new Vector3(1.8f, rise, back - front), false);
                Box("Stair nosing", gold, new Vector3(side * 5.05f, rise - .018f, front - .012f), new Vector3(1.83f, .035f, .045f), false);
            }
        }
        BakeStage();
        deckCollider.sharedMesh = StoreCollision("DeckCollision", collisionMesh);
        foreach (int side in new[] { -1, 1 }) root.AddComponent<MeshCollider>().sharedMesh = StoreCollision("StairRamp" + side, StairRamp(side));
        var anchor = new GameObject("Elevator placement (deck level)"); anchor.transform.SetParent(root.transform, false); anchor.transform.localPosition = new Vector3(0, .7f, .5f);
        Metadata(); PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); root = null;
    }
    static Mesh StoreCollision(string name, Mesh source)
    {
        string path = Folder + "/RestaurantStage.asset";
        var stored = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => m.name == name);
        if (stored == null) { stored = Object.Instantiate(source); stored.name = name; AssetDatabase.AddObjectToAsset(stored, path); }
        else { stored.Clear(); stored.vertices = source.vertices; stored.triangles = source.triangles; stored.RecalculateNormals(); stored.RecalculateBounds(); EditorUtility.SetDirty(stored); }
        return stored;
    }
    static Mesh StairRamp(int side)
    {
        // Continuous walkable profile for the existing player's zero stepOffset.
        float left = side * 5.05f - .9f, right = side * 5.05f + .9f;
        var profile = new[] { new Vector2(-6.77f, 0), new Vector2(-4.61f, .6f), new Vector2(-2.8f, .6f), new Vector2(-2.8f, 0) };
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2); }
        Vector3 P(int index, float x) => new Vector3(x, profile[index].y, profile[index].x);
        for (int i = 0; i < profile.Length; i++)
        {
            int j = (i + 1) % profile.Length; Tri(P(i, left), P(j, left), P(i, right)); Tri(P(i, right), P(j, left), P(j, right));
        }
        Tri(P(0, left), P(2, left), P(1, left)); Tri(P(0, left), P(3, left), P(2, left));
        Tri(P(0, right), P(1, right), P(2, right)); Tri(P(0, right), P(2, right), P(3, right));
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); temporary.Add(mesh); return mesh;
    }
    static void BakeStage()
    {
        var parts = root.GetComponentsInChildren<MeshFilter>(); var groups = parts.GroupBy(p => p.GetComponent<Renderer>().sharedMaterial).ToArray();
        var instances = new List<CombineInstance>();
        foreach (var group in groups)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; temporary.Add(mesh);
            mesh.CombineMeshes(group.Select(p => new CombineInstance { mesh = p.sharedMesh, transform = p.transform.localToWorldMatrix }).ToArray());
            instances.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.identity });
        }
        var combined = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; combined.CombineMeshes(instances.ToArray(), false);
        combined = PropMeshAssets.Store(Folder, "RestaurantStage", combined);
        foreach (var part in parts) Object.DestroyImmediate(part.gameObject);
        var model = new GameObject("Model", typeof(MeshFilter), typeof(MeshRenderer)); model.transform.SetParent(root.transform, false);
        model.GetComponent<MeshFilter>().sharedMesh = combined; model.GetComponent<Renderer>().sharedMaterials = groups.Select(g => g.Key).ToArray();
    }
}
