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

public static class ConfigureWoodenDoor
{
    const string Folder = "Assets/Models/WoodenDoor";
    static Material walnut, oak, brass, dark;
    static Transform parent;
    static readonly List<Mesh> temporary = new();
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
        const string path = "Assets/Prefabs/WoodenDoor.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            var existing = PrefabUtility.LoadPrefabContents(path);
            try { AddInteraction(existing); PrefabUtility.SaveAsPrefabAsset(existing, path); }
            finally { PrefabUtility.UnloadPrefabContents(existing); }
            return "Door interaction upgraded; existing meshes and materials preserved: " + path;
        }
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/WallFurnishings/" + name + ".mat");
        walnut = Mat("Walnut"); oak = Mat("Wicker"); brass = Mat("Brass"); dark = Mat("Charcoal");
        if (walnut == null || oak == null || brass == null || dark == null) throw new Exception("Missing shared furniture materials");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("WoodenDoor"); SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var door = root.AddComponent<EmbeddedDoor>();
            Transform Group(string name) { var g = new GameObject(name); g.transform.SetParent(root.transform, false); return parent = g.transform; }
            door.jamb = Group("Jamb");
            foreach (int side in new[] { -1, 1 })
            {
                Box("Upright", walnut, new Vector3(side * .60f, 1.175f, 0), new Vector3(.1f, 2.35f, 1));
                Collision(new Vector3(side * .60f, 1.175f, 0), new Vector3(.1f, 2.35f, 1));
            }
            Box("Header", walnut, new Vector3(0, 2.29f, 0), new Vector3(1.1f, .12f, 1));
            Collision(new Vector3(0, 2.29f, 0), new Vector3(1.1f, .12f, 1));
            Bake("Jamb");
            foreach (int face in new[] { 1, -1 })
            {
                var trim = Group(face > 0 ? "Front trim" : "Back trim");
                if (face > 0) door.frontTrim = trim; else door.backTrim = trim;
                foreach (int side in new[] { -1, 1 })
                {
                    Box("Architrave", walnut, new Vector3(side * .61f, 1.175f, 0), new Vector3(.13f, 2.35f, .07f));
                    Box("Stepped molding", oak, new Vector3(side * .605f, 1.175f, face * .04f), new Vector3(.08f, 2.35f, .025f));
                    Box("Brass inlay", brass, new Vector3(side * .63f, 1.175f, face * .055f), new Vector3(.008f, 2.28f, .008f));
                    Box("Foot block", walnut, new Vector3(side * .61f, .12f, face * .025f), new Vector3(.15f, .24f, .10f));
                }
                Box("Crown", walnut, new Vector3(0, 2.32f, 0), new Vector3(1.4f, .14f, .08f));
                Box("Crown molding", oak, new Vector3(0, 2.32f, face * .05f), new Vector3(1.31f, .075f, .025f));
                Box("Crown inlay", brass, new Vector3(0, 2.335f, face * .067f), new Vector3(1.27f, .009f, .008f));
                Bake(face > 0 ? "FrontTrim" : "BackTrim");
            }
            Group("Door leaf");
            Box("Solid leaf", oak, new Vector3(0, 1.125f, 0), new Vector3(1.08f, 2.21f, .07f));
            Collision(new Vector3(0, 1.125f, 0), new Vector3(1.08f, 2.21f, .07f));
            foreach (int face in new[] { -1, 1 })
            {
                foreach (float y in new[] { .55f, 1.63f })
                {
                    float h = y < 1 ? .78f : .90f;
                    Box("Recess shadow", walnut, new Vector3(0, y, face * .039f), new Vector3(.88f, h, .012f));
                    Box("Raised panel", oak, new Vector3(0, y, face * .047f), new Vector3(.77f, h - .11f, .018f));
                    foreach (int side in new[] { -1, 1 })
                    {
                        Box("Panel vertical molding", walnut, new Vector3(side * .435f, y, face * .052f), new Vector3(.022f, h, .02f));
                        Box("Panel horizontal molding", walnut, new Vector3(0, y + side * h * .5f, face * .052f), new Vector3(.89f, .022f, .02f));
                    }
                }
                Box("Handle backplate", brass, new Vector3(.4f, 1.09f, face * .046f), new Vector3(.07f, .25f, .018f));
                Rod("Handle stem", brass, new Vector3(.4f, 1.14f, face * .05f), new Vector3(.4f, 1.14f, face * .115f), .032f);
                Rod("Lever handle", brass, new Vector3(.4f, 1.14f, face * .115f), new Vector3(.245f, 1.14f, face * .115f), .035f);
                Box("Keyhole", dark, new Vector3(.4f, 1.02f, face * .058f), new Vector3(.013f, .033f, .008f));
                Box("Bottom rail", walnut, new Vector3(0, .08f, face * .042f), new Vector3(1.04f, .085f, .015f));
            }
            foreach (float y in new[] { .34f, 1.12f, 1.94f })
                Rod("Hinge barrel", brass, new Vector3(-.548f, y - .065f, 0), new Vector3(-.548f, y + .065f, 0), .035f);
            Bake("Leaf");
            door.FitDepth(.2f);
            var prop = root.AddComponent<LevelProp>(); prop.surface = PropSurface.Wall;
            prop.surfaceAnchor = Vector3.zero; prop.surfaceOffset = 0;
            prop.gameplayMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            AddInteraction(root);
            PrefabUtility.SaveAsPrefabAsset(root, path); AssetDatabase.SaveAssets();
            return path;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var mesh in temporary) if (mesh != null) Object.DestroyImmediate(mesh);
            temporary.Clear();
        }
    }
    static void AddInteraction(GameObject root)
    {
        var hinge = root.transform.Find("Hinge");
        if (hinge == null)
        {
            var leaf = root.transform.Find("Door leaf");
            if (leaf == null) throw new Exception("Door leaf is missing.");
            hinge = new GameObject("Hinge").transform; hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(-.55f, 0, 0);
            leaf.SetParent(hinge, true);
        }
        var body = hinge.GetComponent<Rigidbody>();
        if (body == null) body = hinge.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true; body.useGravity = false; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var action = root.GetComponent<DoorInteraction>();
        if (action == null) action = root.AddComponent<DoorInteraction>();
        action.hinge = hinge; action.leafCollider = hinge.GetComponentInChildren<BoxCollider>();
        var anchor = hinge.Find("Interaction anchor");
        if (anchor == null)
        {
            anchor = new GameObject("Interaction anchor").transform; anchor.SetParent(hinge, false);
            anchor.localPosition = new Vector3(.87f, 1.14f, 0);
        }
        action.interactionAnchor = anchor;
    }
    static void Box(string name, Material material, Vector3 position, Vector3 size) => Part(name, PrimitiveType.Cube, material, position, size, Quaternion.identity);
    static void Rod(string name, Material material, Vector3 a, Vector3 b, float diameter) =>
        Part(name, PrimitiveType.Cylinder, material, (a + b) * .5f, new Vector3(diameter, (b-a).magnitude * .5f, diameter), Quaternion.FromToRotation(Vector3.up, b-a));
    static void Part(string name, PrimitiveType type, Material material, Vector3 position, Vector3 size, Quaternion rotation)
    {
        var part = GameObject.CreatePrimitive(type); part.name = name; part.transform.SetParent(parent, false);
        part.transform.localPosition = position; part.transform.localRotation = rotation; part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(part.GetComponent<Collider>());
    }
    static void Collision(Vector3 center, Vector3 size) { var c = parent.gameObject.AddComponent<BoxCollider>(); c.center = center; c.size = size; }
    static void Bake(string name)
    {
        var parts = parent.GetComponentsInChildren<MeshFilter>();
        var groups = parts.GroupBy(p => p.GetComponent<Renderer>().sharedMaterial).ToArray();
        var submeshes = new List<CombineInstance>();
        foreach (var group in groups)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; temporary.Add(mesh);
            mesh.CombineMeshes(group.Select(p => new CombineInstance { mesh = p.sharedMesh,
                transform = parent.worldToLocalMatrix * p.transform.localToWorldMatrix }).ToArray());
            submeshes.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.identity });
        }
        var combined = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        combined.CombineMeshes(submeshes.ToArray(), false);
        AssetDatabase.CreateAsset(combined, Folder + "/" + name + ".asset");
        foreach (var part in parts) Object.DestroyImmediate(part.gameObject);
        parent.gameObject.AddComponent<MeshFilter>().sharedMesh = combined;
        parent.gameObject.AddComponent<MeshRenderer>().sharedMaterials = groups.Select(g => g.Key).ToArray();
    }
}
