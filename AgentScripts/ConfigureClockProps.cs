using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public static class ConfigureClockProps
{
    const string PrefabPath = "Assets/Prefabs/TickingClock.prefab";
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Exit Play mode before authoring.");
        Directory.CreateDirectory("Assets/Models/Clock");
        AssetDatabase.Refresh();
        string meshPath = "Assets/Models/Clock/ClockRing.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) { mesh = Ring(); AssetDatabase.CreateAsset(mesh, meshPath); }
        string materialPath = "Assets/Models/Clock/ClockPreview.mat";
        var preview = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (preview == null)
        {
            preview = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "ClockPreview", color = new Color(.83f, .85f, .8f) };
            AssetDatabase.CreateAsset(preview, materialPath);
        }
        var black = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var stage = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("TickingClock");
                SceneManager.MoveGameObjectToScene(root, stage);
                var ring = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                ring.transform.SetParent(root.transform, false);
                ring.GetComponent<MeshFilter>().sharedMesh = mesh;
                ring.GetComponent<MeshRenderer>().sharedMaterial = preview;
                ring.GetComponent<MeshCollider>().sharedMesh = mesh;
                Transform Hand(string name, float length, float width, float z, float angle)
                {
                    var pivot = new GameObject(name).transform;
                    pivot.SetParent(root.transform, false);
                    pivot.localPosition = new Vector3(0, 0, z);
                    pivot.localRotation = Quaternion.Euler(0, 0, angle);
                    var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bar.name = "Bar"; bar.transform.SetParent(pivot, false);
                    bar.transform.localPosition = new Vector3(0, length * .5f, 0);
                    bar.transform.localScale = new Vector3(width, length, .035f);
                    UnityEngine.Object.DestroyImmediate(bar.GetComponent<Collider>());
                    bar.GetComponent<MeshRenderer>().sharedMaterial = preview;
                    return pivot;
                }
                var longHand = Hand("Long Hand", .45f, .055f, .105f, 0);
                var shortHand = Hand("Short Hand", .29f, .075f, .15f, 90);
                var origin = new GameObject("Tick Origin").transform;
                origin.SetParent(root.transform, false); origin.localPosition = new Vector3(0, 0, .23f);
                var audio = root.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 1;
                var cue = root.AddComponent<SonarCueSource>(); cue.output = audio; cue.generateDefaultPing = false;
                var emitter = root.AddComponent<SonarObjectEmitter>();
                emitter.automatic = false; emitter.toggleKey = Key.None; emitter.emissionOrigin = origin;
                emitter.range = 8; emitter.soundRange = 12; emitter.afterglow = .7f; emitter.waveSpeed = 7;
                var clock = root.AddComponent<TickingClock>(); clock.longHand = longHand; clock.shortHand = shortHand;
                var prop = root.AddComponent<LevelProp>(); prop.surface = PropSurface.Wall; prop.surfaceOffset = .015f; prop.gameplayMaterial = black;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(stage); }
        }
        var clockContents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var clock = clockContents.GetComponent<TickingClock>();
            clock.longHand.localRotation = Quaternion.Euler(0, 0, clock.initialLongStep * 30);
            clock.shortHand.localRotation = Quaternion.Euler(0, 0, clock.initialShortStep * 30);
            PrefabUtility.SaveAsPrefabAsset(clockContents, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(clockContents); }
        string ballPath = "Assets/Prefabs/PickupBouncyBall.prefab";
        var ball = PrefabUtility.LoadPrefabContents(ballPath);
        try
        {
            var prop = ball.GetComponent<LevelProp>();
            if (prop == null)
            {
                prop = ball.AddComponent<LevelProp>(); prop.surface = PropSurface.Floor;
                prop.surfaceAnchor = new Vector3(0, -.5f, 0); prop.gameplayMaterial = black;
                ball.GetComponent<MeshRenderer>().sharedMaterial = preview;
                PrefabUtility.SaveAsPrefabAsset(ball, ballPath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(ball); }
        AssetDatabase.SaveAssets();
        return new { prefab = PrefabPath, ballMetadata = true };
    }

    static Mesh Ring()
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b-a, c-a), normal) < 0) { var swap = b; b = d; d = swap; }
            int index = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
            triangles.AddRange(new[] { index, index+1, index+2, index, index+2, index+3 });
        }
        Vector3 P(float angle, float radius, float z) => new Vector3(Mathf.Cos(angle)*radius, Mathf.Sin(angle)*radius, z);
        for (int i=0; i<96; i++)
        {
            float a = i*Mathf.PI*2/96, b = (i+1)*Mathf.PI*2/96;
            var normal = P((a+b)*.5f, 1, 0);
            Quad(P(a,.6f,0), P(b,.6f,0), P(b,.6f,.08f), P(a,.6f,.08f), normal);
            Quad(P(a,.51f,0), P(b,.51f,0), P(b,.51f,.08f), P(a,.51f,.08f), -normal);
            Quad(P(a,.6f,.08f), P(b,.6f,.08f), P(b,.51f,.08f), P(a,.51f,.08f), Vector3.forward);
            Quad(P(a,.6f,0), P(b,.6f,0), P(b,.51f,0), P(a,.51f,0), Vector3.back);
        }
        var mesh = new Mesh { name = "ClockRing" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }

    public static object PlaceInTest()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/test.unity");
        if (!scene.IsValid() || !scene.isLoaded) throw new Exception("Open test scene before placing the clock.");
        var layout = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelLayout>()).Single();
        var existing = layout.GetComponentsInChildren<TickingClock>(true).FirstOrDefault();
        if (existing != null) return new { name = existing.name, alreadyPresent = true };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var player = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerController>()).FirstOrDefault();
        Vector3 origin = player != null ? player.transform.position : layout.World(layout.rooms[0].center);
        origin.y = layout.transform.position.y + 1.8f;
        Pose bestPose = default; float best = float.PositiveInfinity;
        // Prefer a wall in front of the authored player, then try other directions in the room.
        var forward = player != null ? player.transform.forward : layout.transform.forward;
        for (int i=0; i<24; i++)
        {
            var direction = Quaternion.AngleAxis(i*15, layout.transform.up)*forward;
            if (!LevelPropPlacement.TryPose(layout, prefab, new Ray(origin, direction), PropSurface.Wall, .015f, 0, out var pose)) continue;
            float score = Vector3.Distance(origin,pose.position) + (1-Vector3.Dot(direction,forward))*3;
            if (score < best) { best = score; bestPose = pose; }
        }
        if (float.IsPositiveInfinity(best)) throw new Exception("No nearby wall available for clock placement.");
        Directory.CreateDirectory("Logs/ClockProps");
        if (!EditorSceneManager.SaveScene(scene, "Logs/ClockProps/test-before-clock.unity", true)) throw new Exception("Could not snapshot current scene.");
        var instance = LevelPropPlacement.Place(layout, prefab, bestPose);
        Selection.activeGameObject = instance;
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save test scene.");
        var window = EditorWindow.GetWindow<LevelDesignerWindow>("关卡编辑器"); window.Bind(layout);
        Selection.activeGameObject = instance;
        SceneView.lastActiveSceneView?.FrameSelected();
        return new { scene = scene.path, clock = instance.name, position = instance.transform.position.ToString(), rotation = instance.transform.eulerAngles.ToString() };
    }
}
