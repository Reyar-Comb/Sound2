using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ConsolidatePropMeshes
{
    static readonly Dictionary<Mesh, Mesh> replacements = new();
    static readonly List<string> report = new();
    static string backup;
    static int references;

    static void Backup(string path)
    {
        foreach (string source in new[] { path, path + ".meta" })
        {
            if (!File.Exists(source)) continue;
            string target = backup + "/" + source;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, false);
        }
    }

    // Compare actual geometry, including normals and UVs, independently of asset IDs.
    static string Signature(Mesh mesh)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        void V3(Vector3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
        writer.Write(mesh.vertexCount); writer.Write((int)mesh.indexFormat);
        foreach (var v in mesh.vertices) V3(v);
        foreach (var n in mesh.normals) V3(n);
        foreach (var t in mesh.tangents) { writer.Write(t.x); writer.Write(t.y); writer.Write(t.z); writer.Write(t.w); }
        for (int channel = 0; channel < 8; channel++)
        {
            var uvs = new List<Vector4>(); mesh.GetUVs(channel, uvs); writer.Write(uvs.Count);
            foreach (var uv in uvs) { writer.Write(uv.x); writer.Write(uv.y); writer.Write(uv.z); writer.Write(uv.w); }
        }
        foreach (var c in mesh.colors32) { writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }
        V3(mesh.bounds.center); V3(mesh.bounds.size); writer.Write(mesh.subMeshCount);
        for (int i = 0; i < mesh.subMeshCount; i++)
        { writer.Write((int)mesh.GetTopology(i)); foreach (int index in mesh.GetIndices(i)) writer.Write(index); }
        writer.Flush();
        using var hash = SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
    }

    static bool Replace(Object target)
    {
        if (target == null || target is Mesh) return false;
        using var serialized = new SerializedObject(target);
        var property = serialized.GetIterator(); bool changed = false;
        while (property.Next(true))
            if (property.propertyType == SerializedPropertyType.ObjectReference
                && property.objectReferenceValue is Mesh old && replacements.TryGetValue(old, out var next))
            { property.objectReferenceValue = next; changed = true; references++; }
        if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
        return changed;
    }

    static bool Hierarchy(GameObject root)
    {
        bool changed = false;
        foreach (var component in root.GetComponentsInChildren<Component>(true)) changed |= Replace(component);
        return changed;
    }

    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Exit Play mode before migrating mesh assets.");
        replacements.Clear(); report.Clear(); references = 0;
        backup = "Logs/MeshConsolidation/Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var sources = AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Models/BreakableProps", "Assets/Models/Clock", "Assets/Models/HouseholdProps" })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(p => p.EndsWith(".asset")).ToArray();
        var allAssets = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/")
            && (p.EndsWith(".prefab") || p.EndsWith(".unity") || p.EndsWith(".asset") || p.EndsWith(".mat"))).ToArray();
        var sourceSet = sources.ToHashSet();
        var dependents = allAssets.Where(p => !sourceSet.Contains(p)
            && AssetDatabase.GetDependencies(p).Any(sourceSet.Contains)).ToArray();
        foreach (string path in sources.Concat(dependents).Distinct()) Backup(path);

        var obsolete = new List<string>();
        var obsoleteGuids = new List<string>();
        var groups = sources.GroupBy(p => PropMeshAssets.PathFor(Path.GetDirectoryName(p).Replace('\\', '/'),
            AssetDatabase.LoadAssetAtPath<Mesh>(p).name)).ToArray();
        foreach (var group in groups)
        {
            string target = group.Key;
            if (AssetDatabase.LoadMainAssetAtPath(target) == null)
            {
                string main = group.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "GlassMug") ?? group.First();
                string error = AssetDatabase.MoveAsset(main, target);
                if (!string.IsNullOrEmpty(error)) throw new Exception(error);
                report.Add("MOVE " + main + " -> " + target);
            }
            foreach (string source in group)
            {
                if (source == target || AssetDatabase.LoadMainAssetAtPath(source) == null) continue;
                foreach (var old in AssetDatabase.LoadAllAssetsAtPath(source).OfType<Mesh>())
                {
                    var next = AssetDatabase.LoadAllAssetsAtPath(target).OfType<Mesh>().SingleOrDefault(m => m.name == old.name);
                    if (next == null)
                    {
                        next = Object.Instantiate(old); next.name = old.name;
                        AssetDatabase.AddObjectToAsset(next, target);
                    }
                    if (Signature(old) != Signature(next)) throw new Exception("Geometry changed: " + source);
                    replacements.Add(old, next);
                }
                obsolete.Add(source); obsoleteGuids.Add(AssetDatabase.AssetPathToGUID(source));
            }
            AssetDatabase.SaveAssets();
            report.Add(target + " meshes=" + AssetDatabase.LoadAllAssetsAtPath(target).OfType<Mesh>().Count());
        }

        foreach (string path in dependents.Where(p => p.EndsWith(".prefab")))
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (Hierarchy(root)) { PrefabUtility.SaveAsPrefabAsset(root, path); report.Add("REMAP " + path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var active = SceneManager.GetActiveScene();
        try
        {
            foreach (string path in dependents.Where(p => p.EndsWith(".unity")))
            {
                var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    bool changed = false;
                    foreach (var root in scene.GetRootGameObjects()) changed |= Hierarchy(root);
                    if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); report.Add("REMAP " + path); }
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
        }
        finally { SceneManager.SetActiveScene(active); }
        foreach (string path in dependents.Where(p => p.EndsWith(".asset") || p.EndsWith(".mat")))
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path)) Replace(obj);
        AssetDatabase.SaveAssets();

        // Do not remove originals until every serialized project reference has migrated.
        foreach (string path in allAssets.Except(obsolete))
        {
            if (!File.Exists(path)) continue;
            string text = File.ReadAllText(path);
            if (obsoleteGuids.Any(g => text.Contains(g))) throw new Exception("Old mesh reference remains in " + path);
        }
        foreach (string path in obsolete)
            if (!AssetDatabase.DeleteAsset(path)) throw new Exception("Could not remove migrated asset " + path);

        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab.name.EndsWith("Broken")) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { PropFragments.SetLayer(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        report.Add("Remapped references=" + references + "; removed files=" + obsolete.Count);
        File.WriteAllLines("Logs/MeshConsolidation/migration.txt", report);
        return new { meshFilesBefore = sources.Length, meshFilesAfter = groups.Length,
            migratedMeshes = replacements.Count, references, backup, report };
    }
}
