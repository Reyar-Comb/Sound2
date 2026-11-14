using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VerifyWallFurnishings
{
    static readonly string[] Names = { "WallMural", "WallMirror", "WallFlowerBasket", "WallCabinet", "BarCounter" };
    static readonly List<string> checks = new();
    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
    static void Check(bool pass, string label) { checks.Add((pass ? "PASS: " : "FAIL: ") + label); if (!pass) throw new Exception(label); }
    static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
    }
    public static async Task<object> Edit()
    {
        checks.Clear(); Check(!Application.isPlaying, "Authoring checks run outside Play Mode");
        var original = SceneManager.GetActiveScene(); var selection = Selection.objects;
        var windows = Resources.FindObjectsOfTypeAll<LevelDesignerWindow>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var w in windows) typeof(LevelDesignerWindow).GetMethod("OnDisable", flags).Invoke(w, null);
        Scene scene = default, exported = default;
        string id = Guid.NewGuid().ToString("N");
        string source = "Assets/Scenes/__FurnishingSource_" + id + ".unity", destination = "Assets/Scenes/__FurnishingExport_" + id + ".unity";
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Furnishing verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.position = new Vector3(600, 0, 600); layout.transform.rotation = Quaternion.Euler(0, 31, 0);
            layout.rooms.Add(new LevelRoom { size = Vector2.one * 30 }); LevelBuilder.Rebuild(layout);
            foreach (string name in Names)
            {
                var prefab = Prefab(name); Check(prefab != null, name + " exists");
                var prop = prefab.GetComponent<LevelProp>();
                Check(prop != null && prop.gameplayMaterial != null && prop.surface == (name == "BarCounter" ? PropSurface.Floor : PropSurface.Wall), name + " uses expected snap surface and gameplay material");
                Check(prefab.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh != null && AssetDatabase.Contains(f.sharedMesh)) && prefab.GetComponentsInChildren<Collider>().Length > 0, name + " has persistent meshes and colliders");
                Check(prefab.GetComponent<Rigidbody>() == null && prefab.GetComponent<PickupItem>() == null, name + " stays fixed without unsolicited pickup or pushing");
                if (name == "BarCounter") continue;
                var firstRay = new Ray(layout.World(new Vector2(-6 + Array.IndexOf(Names, name) * 3, 0), 2), layout.transform.forward);
                Check(LevelPropPlacement.TryPose(layout, prefab, firstRay, PropSurface.Wall, .01f, 0, out var pose), name + " snaps to rotated wall");
                var instance = LevelPropPlacement.Place(layout, prefab, pose); var old = instance.transform.position;
                var secondRay = new Ray(layout.World(new Vector2(0, -6 + Array.IndexOf(Names, name) * 3), 2), layout.transform.right);
                Check(LevelPropPlacement.TryPose(layout, instance, secondRay, PropSurface.Wall, .01f, 0, out var moved), name + " can be resnapped");
                LevelPropPlacement.Reposition(layout, instance, moved);
                Undo.PerformUndo(); Check(Vector3.Distance(instance.transform.position, old) < .002f, name + " resnap Undo restores position");
                Undo.PerformRedo(); Check(Vector3.Distance(instance.transform.position, moved.position) < .002f && PrefabUtility.IsPartOfPrefabInstance(instance), name + " resnap Redo retains Prefab link");
            }
            var mural = Prefab("WallMural").GetComponentInChildren<MeshFilter>();
            var mats = mural.GetComponent<Renderer>().sharedMaterials; int painted = Array.FindIndex(mats, m => m.name == "PaintedCanvas");
            var zs = mural.sharedMesh.GetTriangles(painted).Select(i => mural.sharedMesh.vertices[i].z).ToArray();
            Check(painted >= 0 && mats[painted].mainTexture != null && zs.Max() - zs.Min() < .00001f && Math.Abs(zs[0] - .051f) < .001f, "Mural artwork is a single flat textured canvas without relief");
            var barPrefab = Prefab("BarCounter");
            Check(LevelPropPlacement.TryPose(layout, barPrefab, new Ray(layout.World(Vector2.zero, 3), -layout.transform.up), PropSurface.Floor, .01f, 0, out var barPose), "Bar snaps to floor");
            var bar = LevelPropPlacement.Place(layout, barPrefab, barPose).GetComponent<ResizableBar>();
            foreach (float length in new[] { .8f, 1.37f, 2.4f, 6.75f, 12.34f })
            {
                var serialized = new SerializedObject(bar); serialized.FindProperty("length").floatValue = length; serialized.ApplyModifiedProperties();
                await Task.Delay(150); Physics.SyncTransforms();
                var top = bar.transform.Find("Stone countertop"); var end = bar.transform.Find("Right end");
                Check(Mathf.Abs(top.localScale.x - length - .16f) < .001f && Mathf.Abs(end.localPosition.x - (length * .5f - .033f)) < .001f, "Bar length " + length + " updates span and end positions through inspector validation");
                Check(Mathf.Abs(end.Find("End panel").localScale.x - .065f) < .0001f, "Bar length " + length + " keeps end panel thickness");
                var point = bar.transform.TransformPoint(new Vector3(length * .5f - .03f, 3, 0));
                var collider = top.GetComponent<BoxCollider>();
                Check(collider.Raycast(new Ray(point, -bar.transform.up), out var hit, 5) && Mathf.Abs(bar.transform.InverseTransformPoint(hit.point).y - 1.12f) < .002f, "Bar length " + length + " keeps countertop collision aligned at the extended edge");
            }
            Undo.IncrementCurrentGroup(); Undo.RecordObject(bar, "Resize bar verification"); bar.length = 4.73f; bar.Rebuild();
            PrefabUtility.RecordPrefabInstancePropertyModifications(bar); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); await Task.Delay(150);
            Check(Mathf.Abs(bar.length - 12.34f) < .001f && Mathf.Abs(bar.transform.Find("Stone countertop").localScale.x - 12.50f) < .001f, "Undo restores bar length and geometry");
            Undo.PerformRedo(); await Task.Delay(150);
            Check(Mathf.Abs(bar.length - 4.73f) < .001f && Mathf.Abs(bar.transform.Find("Stone countertop").localScale.x - 4.89f) < .001f, "Redo restores bar length and geometry");
            var cup = Prefab("PickupWaterCup");
            Check(LevelPropPlacement.TryPose(layout, cup, new Ray(bar.transform.TransformPoint(new Vector3(1.8f, 3, 0)), -bar.transform.up), PropSurface.Floor, .01f, 0, out var cupPose), "Other props snap onto the extended bar countertop");
            LevelBuilder.Persist(layout, source); EditorSceneManager.SaveScene(scene, source);
            LevelBuilder.Export(layout, destination, false); exported = EditorSceneManager.OpenScene(destination, OpenSceneMode.Additive);
            var copy = exported.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ResizableBar>()).Single();
            Check(Mathf.Abs(copy.length - 4.73f) < .001f && Mathf.Abs(copy.transform.Find("Stone countertop").localScale.x - 4.89f) < .001f && PrefabUtility.IsPartOfPrefabInstance(copy), "Export and reopen retain bar length, generated dimensions and Prefab link");
            Check(exported.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelProp>()).Count() == 5, "Export retains all five new furnishings");
            AuditPushCandidates();
            return new { passed = true, checks };
        }
        finally
        {
            if (exported.IsValid() && exported.isLoaded) EditorSceneManager.CloseScene(exported, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            foreach (var path in new[] { source, destination })
            {
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                var geometry = Path.ChangeExtension(path, null) + "_Geometry"; if (Directory.Exists(geometry)) AssetDatabase.DeleteAsset(geometry);
            }
            Selection.objects = selection;
            foreach (var w in windows) { typeof(LevelDesignerWindow).GetMethod("OnEnable", flags).Invoke(w, null); typeof(LevelDesignerWindow).GetMethod("Refresh", flags).Invoke(w, null); }
            Directory.CreateDirectory("Logs/WallFurnishings"); File.WriteAllLines("Logs/WallFurnishings/edit.txt", checks);
        }
    }
    static void AuditPushCandidates()
    {
        var names = new[] { "ShoppingCart", "Chair", "TrashBin", "ResizableTable", "WritingDesk", "LibraryBookshelf", "Refrigerator", "Stove", "BarCounter", "WallCabinet", "WallMirror", "WallMural", "WallFlowerBasket", "Television", "Computer", "Radio", "TapeRecorder", "HeartRateMonitor", "ServiceBell", "GameConsole", "Ring", "PickupWaterCup", "PickupDeskLamp" };
        var rows = new List<string> { "Prefab,Width,Height,Depth,Rigidbodies,Colliders,NonConvexMeshColliders,Pickup,Surface" };
        foreach (string name in names)
        {
            var prefab = Prefab(name); var b = BoundsOf(prefab).size;
            rows.Add($"{name},{b.x:F3},{b.y:F3},{b.z:F3},{prefab.GetComponentsInChildren<Rigidbody>().Length},{prefab.GetComponentsInChildren<Collider>().Length},{prefab.GetComponentsInChildren<MeshCollider>().Count(c => !c.convex)},{prefab.GetComponent<PickupItem>() != null},{prefab.GetComponent<LevelProp>().surface}");
        }
        Directory.CreateDirectory("Logs/WallFurnishings"); File.WriteAllLines("Logs/WallFurnishings/push-candidates.csv", rows);
    }
    public static object Preview()
    {
        var atlas = new Texture2D(1920, 1040, TextureFormat.RGB24, false);
        try
        {
            for (int i = 0; i < 6; i++)
            {
                var preview = new PreviewRenderUtility();
                try
                {
                    var prefab = Prefab(Names[Mathf.Min(i, 4)]);
                    var model = Object.Instantiate(prefab);
                    model.GetComponent<LevelProp>().enabled = false;
                    var originalRenderers = prefab.GetComponentsInChildren<Renderer>();
                    var previewRenderers = model.GetComponentsInChildren<Renderer>();
                    for (int r = 0; r < previewRenderers.Length; r++) previewRenderers[r].sharedMaterials = originalRenderers[r].sharedMaterials;
                    preview.AddSingleGO(model);
                    if (i == 5) { model.GetComponent<ResizableBar>().length = 5.5f; model.GetComponent<ResizableBar>().Rebuild(); }
                    preview.lights[0].intensity = 2.5f; preview.lights[0].transform.rotation = Quaternion.Euler(45, 210, 0);
                    preview.lights[1].intensity = .75f; preview.lights[1].transform.rotation = Quaternion.Euler(20, 110, 0);
                    var cam = preview.camera; cam.cameraType = CameraType.Preview; cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(.055f, .07f, .09f); cam.orthographic = true; cam.nearClipPlane = .01f; cam.farClipPlane = 50;
                    var bounds = BoundsOf(model); float extent = Mathf.Max(bounds.size.x / (640f / 520), bounds.size.y);
                    cam.orthographicSize = Mathf.Max(.5f, extent * .72f);
                    cam.transform.position = bounds.center + new Vector3(i == 0 ? .15f : .85f, i < 3 ? .35f : .8f, 2.1f).normalized * 10;
                    cam.transform.LookAt(bounds.center);
                    preview.BeginStaticPreview(new Rect(0, 0, 640, 520)); preview.Render(true);
                    var frame = preview.EndStaticPreview();
                    atlas.SetPixels(i % 3 * 640, (1 - i / 3) * 520, 640, 520, frame.GetPixels()); Object.DestroyImmediate(frame);
                }
                finally { preview.Cleanup(); }
            }
            atlas.Apply(); Directory.CreateDirectory("Logs/WallFurnishings"); File.WriteAllBytes("Logs/WallFurnishings/models.png", atlas.EncodeToPNG());
            return "Logs/WallFurnishings/models.png";
        }
        finally
        {
            Object.DestroyImmediate(atlas);
        }
    }
}
