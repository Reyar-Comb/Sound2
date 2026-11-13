using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AddBlackBoxBall
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/BlackBoxPointCloud.unity")
            throw new InvalidOperationException("Open BlackBoxPointCloud first.");
        var bounce = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Materials/CubeRoom/ProbeBounce.physicMaterial");
        var visual = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        if (bounce == null || visual == null) throw new InvalidOperationException("Missing ball materials.");
        const string prefabPath = "Assets/Prefabs/PickupBouncyBall.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                ball.name = "PickupBouncyBall";
                ball.transform.localScale = Vector3.one * .4f;
                ball.GetComponent<SphereCollider>().sharedMaterial = bounce;
                ball.GetComponent<MeshRenderer>().sharedMaterial = visual;
                var body = ball.AddComponent<Rigidbody>();
                body.mass = .3f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.linearDamping = .02f;
                ball.AddComponent<PickupItem>();
                prefab = PrefabUtility.SaveAsPrefabAsset(ball, prefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(ball); }
        }
        var instance = GameObject.Find("PickupBouncyBall");
        if (instance == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add pickup bouncy ball");
            instance.transform.position = new Vector3(.65f, .21f, 4.25f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = instance;
        return new { scene = scene.path, prefabPath, position = instance.transform.position.ToString(), bounciness = bounce.bounciness };
    }
}
