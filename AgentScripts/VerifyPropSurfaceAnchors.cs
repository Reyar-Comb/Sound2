using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VerifyPropSurfaceAnchors
{
    static readonly List<string> checks = new();
    static void Check(bool pass, string name)
    {
        checks.Add((pass ? "PASS: " : "FAIL: ") + name);
        if (!pass) throw new Exception(name);
    }
    static IEnumerable<Vector3> Corners(GameObject source)
    {
        foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var b = filter.sharedMesh.bounds;
            var m = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++) yield return m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
        }
    }
    public static object Audit()
    {
        var rows = PropCatalog.All().Where(p => p.GetComponent<EmbeddedDoor>() == null).Select(p =>
        {
            var points = Corners(p).ToArray(); var meta = p.GetComponent<LevelProp>();
            return new { name = p.name, label = PropCatalog.Name(p), surface = meta.surface.ToString(),
                floorGap = points.Min(v => v.y) - meta.surfaceAnchor.y,
                wallGap = points.Min(v => v.z) - meta.surfaceAnchor.z };
        }).ToArray();
        Directory.CreateDirectory("Logs/PropSurfaceAnchors");
        File.WriteAllLines("Logs/PropSurfaceAnchors/before.txt", rows.Select(r => $"{r.name} ({r.label}) default={r.surface} floorGap={r.floorGap:F4} wallGap={r.wallGap:F4}"));
        return rows;
    }
    public static object Run()
    {
        checks.Clear(); Check(!Application.isPlaying, "Verification runs in Edit Mode");
        var original = SceneManager.GetActiveScene(); var selection = Selection.objects;
        var windows = Resources.FindObjectsOfTypeAll<LevelDesignerWindow>();
        void Call(LevelDesignerWindow w, string method) => typeof(LevelDesignerWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(w, null);
        foreach (var w in windows) Call(w, "OnDisable");
        Scene scene = default;
        string path = "Assets/Scenes/__SurfaceAnchors_" + Guid.NewGuid().ToString("N") + ".unity";
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Surface anchor verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.SetPositionAndRotation(new Vector3(700, 30, 700), Quaternion.Euler(0, 31, 0));
            layout.generatedRoot = new GameObject("Generated"); layout.generatedRoot.transform.SetParent(layout.transform, false);
            void Box(string name, Vector3 p, Vector3 size)
            {
                var go = new GameObject(name, typeof(BoxCollider)); go.transform.SetParent(layout.generatedRoot.transform, false);
                go.transform.localPosition = p; go.GetComponent<BoxCollider>().size = size;
            }
            Box("Floor", new Vector3(0, -.5f, 0), new Vector3(100, 1, 100));
            Box("Wall", new Vector3(0, 10, 20.5f), new Vector3(100, 40, 1));
            var sources = PropCatalog.All();
            Check(sources.Count == 62 && sources.Count(p => p.GetComponent<EmbeddedDoor>() != null) == 1, "Audit covers 61 ordinary props; embedded door has its dedicated placement path");
            foreach (var prefab in sources.Where(p => p.GetComponent<EmbeddedDoor>() == null))
            foreach (PropSurface surface in new[] { PropSurface.Floor, PropSurface.Wall })
            {
                var hit = layout.transform.TransformPoint(surface == PropSurface.Floor ? Vector3.zero : new Vector3(0, 5, 20));
                var normal = surface == PropSurface.Floor ? layout.transform.up : -layout.transform.forward;
                var ray = new Ray(hit + normal * 8, -normal);
                var label = prefab.name + "/" + surface;
                Check(LevelPropPlacement.TryPose(layout, prefab, ray, surface, .015f, 23, out var pose), label + " resolves rotated surface");
                var previewMatrix = Matrix4x4.TRS(pose.position, pose.rotation, prefab.transform.localScale);
                var preview = Corners(prefab).Select(previewMatrix.MultiplyPoint3x4).ToArray();
                var meta = prefab.GetComponent<LevelProp>();
                if (meta.surface != surface)
                    Check(Mathf.Abs(preview.Min(p => Vector3.Dot(p - hit, normal)) - .015f) < .002f, label + " switched surface sits outside plane at requested gap");
                else
                    Check(Vector3.Distance(previewMatrix.MultiplyPoint3x4(meta.surfaceAnchor), hit + normal * .015f) < .002f, label + " retains authored default anchor");
                var instance = LevelPropPlacement.Place(layout, prefab, pose);
                var placed = Corners(instance).Select(instance.transform.TransformPoint).ToArray();
                Check(placed.Length == preview.Length && placed.Zip(preview, (a, b) => Vector3.Distance(a, b)).All(d => d < .003f), label + " committed mesh matches preview");
                Object.DestroyImmediate(instance);
            }
            var cabinet = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WallCabinet.prefab");
            var floorHit = layout.transform.position; var floorRay = new Ray(floorHit + layout.transform.up * 8, -layout.transform.up);
            Check(LevelPropPlacement.TryPose(layout, cabinet, floorRay, PropSurface.Floor, .015f, 39, out var initial), "Cabinet can switch directly to floor placement");
            var item = LevelPropPlacement.Place(layout, cabinet, initial);
            var before = item.transform.position;
            item.transform.localScale = new Vector3(1.4f, .7f, 1.2f);
            var destination = layout.transform.TransformPoint(new Vector3(3, 0, 2));
            var moveRay = new Ray(destination + layout.transform.up * 8, -layout.transform.up);
            Check(LevelPropPlacement.TryPose(layout, item, moveRay, PropSurface.Floor, .025f, 71, out var moved), "Scaled existing cabinet resolves floor resnap");
            LevelPropPlacement.Reposition(layout, item, moved);
            Check(Mathf.Abs(Corners(item).Select(item.transform.TransformPoint).Min(p => Vector3.Dot(p - destination, layout.transform.up)) - .025f) < .002f, "Nonuniformly scaled cabinet rests above floor");
            Undo.PerformUndo(); Check(Vector3.Distance(item.transform.position, before) < .002f, "Reposition Undo restores cabinet");
            Undo.PerformRedo(); Check(Vector3.Distance(item.transform.position, moved.position) < .002f, "Reposition Redo restores floor alignment");
            Check(item.GetComponent<LevelProp>().surface == PropSurface.Wall && item.GetComponent<LevelProp>().surfaceAnchor == Vector3.zero, "Switching surfaces preserves original wall metadata and prefab");
            var expected = item.transform.position;
            EditorSceneManager.SaveScene(scene, path); EditorSceneManager.CloseScene(scene, true);
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var saved = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelProp>()).Single();
            Check(Vector3.Distance(saved.transform.position, expected) < .002f && PrefabUtility.IsPartOfPrefabInstance(saved), "Save and reopen preserve corrected position and prefab connection");
            return new { passed = true, count = checks.Count, props = sources.Count, checks };
        }
        finally
        {
            if (original.IsValid()) SceneManager.SetActiveScene(original);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            Selection.objects = selection;
            foreach (var w in windows) { Call(w, "OnEnable"); Call(w, "Refresh"); }
            Directory.CreateDirectory("Logs/PropSurfaceAnchors"); File.WriteAllLines("Logs/PropSurfaceAnchors/edit.txt", checks);
        }
    }
}
