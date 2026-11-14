using System;
using UnityEditor;
using UnityEngine;

public static class ConfigureCameraMotion
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode before configuring the prefab.");
        const string path = "Assets/Prefabs/Player.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var motion = root.GetComponent<PlayerCameraMotion>();
            if (motion == null) motion = root.AddComponent<PlayerCameraMotion>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return new { prefab = path, motion.idleHeight, motion.idlePeriod, motion.walkHeight, motion.walkSway, motion.walkRoll };
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
