using System;
using UnityEditor;
using UnityEngine;

public static class ConfigureInteraction
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode before configuring the player prefab.");
        const string path = "Assets/Prefabs/Player.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var interaction = root.GetComponent<PlayerInteraction>();
            if (interaction == null) interaction = root.AddComponent<PlayerInteraction>();
            interaction.viewCamera = root.GetComponentInChildren<Camera>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return new { configured = path, crosshairRadius = 2, clearRadius = 14, ringRadius = 18, aimAngle = 8 };
    }
}
