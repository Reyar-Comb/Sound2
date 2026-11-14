using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VerifyPropMeshPacking
{
    const string Baseline = "Logs/MeshConsolidation/geometry-baseline.txt";
    static readonly List<string> lines = new();
    static string Key(Transform t) => (t.parent == null ? "" : Key(t.parent) + "/") + t.GetSiblingIndex() + ":" + t.name;
    static void Collect(GameObject root, string path)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            Mesh mesh = component is MeshFilter f ? f.sharedMesh : component is MeshCollider c ? c.sharedMesh : null;
            if (mesh == null || !AssetDatabase.GetAssetPath(mesh).StartsWith("Assets/Models/")) continue;
            // Includes every vertex/index/normal/UV in the native serialized Mesh; GUID is external to this data.
            var json = Newtonsoft.Json.Linq.JObject.Parse(EditorJsonUtility.ToJson(mesh));
            // Compare with the original captured names when the two main assets were renamed.
            // Normalize only that display field; all geometry/physics serialization remains in the hash.
            var meshData = (Newtonsoft.Json.Linq.JObject)json["Mesh"];
            if (mesh.name == "AlarmClock") meshData["m_Name"] = "AlarmRim0";
            else if (mesh.name == "CoffeeMug") meshData["m_Name"] = "GlassMug";
            string data = json.ToString(Newtonsoft.Json.Formatting.None);
            using var sha = System.Security.Cryptography.SHA256.Create();
            string signature = Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data)));
            lines.Add(path + "|" + Key(component.transform) + "|" + component.GetType().Name + "|" + signature);
        }
    }
    static string[] Snapshot()
    {
        lines.Clear();
        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath))
            Collect(AssetDatabase.LoadAssetAtPath<GameObject>(path), path);
        var active = SceneManager.GetActiveScene();
        try
        {
            foreach (string path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try { foreach (var root in scene.GetRootGameObjects()) Collect(root, path); }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
        }
        finally { SceneManager.SetActiveScene(active); }
        return lines.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
    public static object Capture()
    {
        if (Application.isPlaying) throw new Exception("Requires edit mode");
        Directory.CreateDirectory("Logs/MeshConsolidation");
        var snapshot = Snapshot(); File.WriteAllLines(Baseline, snapshot);
        return new { capturedMeshReferences = snapshot.Length };
    }
    public static object Verify()
    {
        var before = File.ReadAllLines(Baseline); var after = Snapshot();
        File.WriteAllLines("Logs/MeshConsolidation/geometry-after.txt", after);
        if (!before.SequenceEqual(after)) throw new Exception("Mesh reference/geometry snapshot differs; inspect reference-before/after report.");
        var paths = AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Models" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();
        var meshes = paths.SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>()).ToArray();
        if (paths.Length != 8 || meshes.Length != 196) throw new Exception("Unexpected mesh inventory");
        // Regeneration must preserve the same sub-assets/file IDs, never append duplicate meshes.
        foreach (var mesh in meshes)
        {
            var path = AssetDatabase.GetAssetPath(mesh);
            var stored = PropMeshAssets.Store(Path.GetDirectoryName(path).Replace('\\', '/'), mesh.name, UnityEngine.Object.Instantiate(mesh));
            if (stored != mesh) throw new Exception("Generator failed to reuse " + path + ":" + mesh.name);
        }
        AssetDatabase.SaveAssets();
        if (!before.SequenceEqual(Snapshot())) throw new Exception("Regeneration changed mesh data or references");
        VerifyFreshStore();
        int fragments = 0;
        foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!root.name.EndsWith("Broken")) continue;
            if (root.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.layer != PropFragments.Layer)) throw new Exception("Incorrect fragment layer: " + path);
            if (root.GetComponentsInChildren<InteractableObject>(true).Length != 0) throw new Exception("Pickable fragment: " + path);
            fragments += root.GetComponentsInChildren<Rigidbody>().Length;
        }
        string report = "PASS: " + before.Length + " mesh references unchanged in all prefabs/scenes; 196 meshes in 8 files; " + fragments + " non-pickable fragment bodies on Fragments layer.";
        File.WriteAllText("Logs/MeshConsolidation/edit.txt", report);
        return report;
    }

    static void VerifyFreshStore()
    {
        string root = "Assets/__PropMeshPacking_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(root));
        AssetDatabase.CreateFolder(root, "HouseholdProps");
        string folder = root + "/HouseholdProps";
        Mesh Triangle() => new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        try
        {
            var fragment = PropMeshAssets.Store(folder, "WaterCupWall 0 0", Triangle());
            var main = PropMeshAssets.Store(folder, "WaterCup", Triangle());
            AssetDatabase.SaveAssets();
            string path = folder + "/WaterCup.asset";
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(fragment, out string guid, out long id);
            var updated = PropMeshAssets.Store(folder, "WaterCupWall 0 0", Triangle());
            AssetDatabase.SaveAssets();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(updated, out string afterGuid, out long afterId);
            if (id != afterId || guid != afterGuid || AssetDatabase.LoadMainAssetAtPath(path) != main
                || main.name != "WaterCup" || AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Count() != 2)
                throw new Exception("Fresh generation or repeated generation failed to preserve packed mesh IDs");
            AssetDatabase.CreateFolder(root, "BreakableProps");
            string legacyFolder = root + "/BreakableProps";
            PropMeshAssets.Store(legacyFolder, "MugWallLower0", Triangle(), false);
            var coffee = PropMeshAssets.Store(legacyFolder, "GlassMug", Triangle(), false);
            var alarm = PropMeshAssets.Store(legacyFolder, "AlarmRim0", Triangle(), false);
            AssetDatabase.SaveAssets();
            if (coffee.vertexCount != 3 || coffee.name != "CoffeeMug" || alarm.name != "AlarmClock"
                || PropMeshAssets.Store(legacyFolder, "GlassMug", Triangle(), false) != coffee
                || PropMeshAssets.Store(legacyFolder, "AlarmRim0", Triangle(), false) != alarm)
                throw new Exception("Legacy generator aliases or first-time combined mesh population failed");
        }
        finally { AssetDatabase.DeleteAsset(root); }
    }
}
