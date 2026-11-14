using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>One mesh asset per authored prop, with collision and fragment meshes as sub-assets.</summary>
public static class PropMeshAssets
{
    public static string PathFor(string folder, string meshName)
    {
        string name = meshName;
        if (folder.EndsWith("/BreakableProps", StringComparison.Ordinal))
        {
            if (meshName.StartsWith("AlarmRim", StringComparison.Ordinal)) name = "AlarmClock";
            else if (meshName == "GlassMug" || meshName.StartsWith("Mug", StringComparison.Ordinal)) name = "CoffeeMug";
        }
        else if (folder.EndsWith("/HouseholdProps", StringComparison.Ordinal))
        {
            if (meshName.StartsWith("CeramicVase", StringComparison.Ordinal)) name = "CeramicVase";
            else if (meshName.StartsWith("WineGlass", StringComparison.Ordinal)) name = "WineGlass";
            else if (meshName.StartsWith("WaterCup", StringComparison.Ordinal)) name = "WaterCup";
            else if (meshName.StartsWith("TrashBin", StringComparison.Ordinal) || meshName == "BinRim") name = "TrashBin";
        }
        return folder + "/" + name + ".asset";
    }

    public static Mesh Store(string folder, string name, Mesh mesh, bool updateExisting = true)
    {
        string path = PathFor(folder, name);
        // Main Mesh names follow their container filenames; keep legacy generator names as aliases.
        if (folder.EndsWith("/BreakableProps", StringComparison.Ordinal))
        {
            if (name == "AlarmRim0") name = "AlarmClock";
            else if (name == "GlassMug") name = "CoffeeMug";
        }
        mesh.name = name;
        var existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().SingleOrDefault(m => m.name == name);
        if (existing != null)
        {
            if (updateExisting || existing.vertexCount == 0) EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
        if (AssetDatabase.LoadMainAssetAtPath(path) == null)
        {
            string mainName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == mainName) AssetDatabase.CreateAsset(mesh, path);
            else
            {
                // Vessel builders emit fragments before their combined visual mesh.
                // Reserve that main mesh now; its later Store call fills it in place.
                AssetDatabase.CreateAsset(new Mesh { name = mainName }, path);
                AssetDatabase.AddObjectToAsset(mesh, path);
            }
        }
        else AssetDatabase.AddObjectToAsset(mesh, path);
        return mesh;
    }
}
