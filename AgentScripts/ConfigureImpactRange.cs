using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ConfigureImpactRange
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/BlackBoxPointCloud.unity") throw new InvalidOperationException("Open BlackBox first.");
        var ball = GameObject.Find("PickupBouncyBall");
        var collision = ball.GetComponent<CollisionSonarEmitter>();
        var emitter = ball.GetComponent<SonarObjectEmitter>();
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ball);
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try
        {
            EditorUtility.CopySerialized(collision, prefab.GetComponent<CollisionSonarEmitter>());
            var target = prefab.GetComponent<SonarObjectEmitter>();
            // Scene references stay on the instance. Copy the user's numerical/input tuning only.
            target.range = emitter.range;
            target.soundRange = emitter.soundRange;
            target.waveSpeed = emitter.waveSpeed;
            target.afterglow = emitter.afterglow;
            target.volume = emitter.volume;
            target.playSound = emitter.playSound;
            target.automatic = emitter.automatic;
            target.toggleKey = emitter.toggleKey;
            target.interval = emitter.interval;
            target.firstDelay = emitter.firstDelay;
            PrefabUtility.SaveAsPrefabAsset(prefab, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
        AssetDatabase.SaveAssets();
        return new { collision.minimumImpactSpeed, collision.rangePerSpeed, collision.maximumRange,
            emitter.range, emitter.soundRange, emitter.waveSpeed, emitter.afterglow, emitter.volume, emitter.automatic };
    }
}
