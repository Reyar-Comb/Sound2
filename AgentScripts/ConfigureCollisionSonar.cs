using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public static class ConfigureCollisionSonar
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the open scene first.");
        const string prefabPath = "Assets/Prefabs/PickupBouncyBall.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var collision = prefab.GetComponent<CollisionSonarEmitter>();
            if (collision == null) collision = prefab.AddComponent<CollisionSonarEmitter>();
            var emitter = prefab.GetComponent<SonarObjectEmitter>();
            emitter.automatic = false;
            emitter.toggleKey = Key.None;
            emitter.range = 12;
            emitter.waveSpeed = 7;
            emitter.afterglow = .7f;
            emitter.playSound = true;
            prefab.GetComponent<AudioSource>().playOnAwake = false;
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/BlackBoxPointCloud.unity");
        var field = UnityEngine.Object.FindAnyObjectByType<SonarPulseField>();
        if (field == null)
        {
            var go = new GameObject("SonarPulseField");
            Undo.RegisterCreatedObjectUndo(go, "Add shared collision sonar field");
            field = go.AddComponent<SonarPulseField>();
        }
        var ball = GameObject.Find("PickupBouncyBall");
        if (ball == null) throw new InvalidOperationException("BlackBox ball is missing.");
        var objectEmitter = ball.GetComponent<SonarObjectEmitter>();
        Undo.RecordObject(objectEmitter, "Bind ball to shared sonar field");
        objectEmitter.pulseField = field;
        PrefabUtility.RecordPrefabInstancePropertyModifications(objectEmitter);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save BlackBox.");
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = ball;
        return new { scene = scene.path, prefabPath, field = field.name, automatic = objectEmitter.automatic };
    }
}
