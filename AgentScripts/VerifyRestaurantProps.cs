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
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class VerifyRestaurantProps
{
    static readonly List<string> checks = new();
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
    static void Check(bool pass, string label) { checks.Add((pass ? "PASS: " : "FAIL: ") + label); if (!pass) throw new Exception(label); }
    static Bounds BoundsOf(GameObject model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
    }
    static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", Flags).Invoke(button.clickable, new object[] { null });
    static bool TopHit(DrapedRingTable table, float angle, out RaycastHit hit)
    {
        float a = angle * Mathf.Deg2Rad, radius = (table.innerRadius + table.outerRadius) * .5f;
        Physics.SyncTransforms(); return table.GetComponent<MeshCollider>().Raycast(new Ray(table.transform.TransformPoint(new Vector3(Mathf.Cos(a) * radius, table.height + 2, Mathf.Sin(a) * radius)), -table.transform.up), out hit, 10);
    }
    public static async Task<object> Edit()
    {
        checks.Clear(); Check(!Application.isPlaying, "Edit checks outside Play Mode");
        var original = SceneManager.GetActiveScene(); var selection = Selection.objects;
        var windows = Resources.FindObjectsOfTypeAll<LevelDesignerWindow>();
        foreach (var window in windows) typeof(LevelDesignerWindow).GetMethod("OnDisable", Flags).Invoke(window, null);
        Scene scene = default, exported = default; LevelDesignerWindow designer = null;
        var id = Guid.NewGuid().ToString("N");
        string source = "Assets/Scenes/__RestaurantSource_" + id + ".unity", destination = "Assets/Scenes/__RestaurantExport_" + id + ".unity", prefabPath = "Assets/Scenes/__RestaurantLayout_" + id + ".prefab";
        try
        {
            Check(PropCatalog.All().Count == 55, "Catalog contains 55 props");
            Check(PropCatalog.Category(Prefab("DrapedRingTable")) == "餐厅" && PropCatalog.Category(Prefab("RestaurantStage")) == "餐厅", "Both new props are in Restaurant");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Restaurant verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.position = new Vector3(800, 0, 800); layout.transform.rotation = Quaternion.Euler(0, 23, 0);
            layout.rooms.Add(new LevelRoom { size = Vector2.one * 50 }); LevelBuilder.Rebuild(layout);
            var instance = LevelPropPlacement.Place(layout, Prefab("DrapedRingTable"), new Pose(layout.World(new Vector2(-10, 0), .01f), layout.transform.rotation));
            var table = instance.GetComponent<DrapedRingTable>(); table.Rebuild();
            var collider = table.GetComponent<MeshCollider>();
            Check(table.GetComponent<MeshFilter>().sharedMesh.vertexCount > 10000 && !collider.convex, "Continuous draped mesh and concave collision preserve hollow center");
            Check(TopHit(table, 0, out var top) && Vector3.Dot(top.normal, table.transform.up) > .99f && Mathf.Abs(table.transform.InverseTransformPoint(top.point).y - table.height) < .001f, "Tabletop faces upward at requested height");
            Check(!collider.Raycast(new Ray(table.transform.TransformPoint(new Vector3(0, 3, 0)), -table.transform.up), out _, 10), "Center hole has no invisible collision");
            for (int i = 0; i < 360; i += 15)
                Check(Mathf.Abs(table.ClothEdge(i, 1, true).y) < .00001f && Mathf.Abs(table.ClothEdge(i, 1, false).y) < .00001f, "Inner and outer hems reach ground at " + i + " degrees");
            var before = table.GetComponent<MeshFilter>().sharedMesh.vertices;
            table.Rebuild(); Check(before.SequenceEqual(table.GetComponent<MeshFilter>().sharedMesh.vertices), "Same parameters reproduce identical folds");
            table.clothSeed++; table.Rebuild(); Check(!before.SequenceEqual(table.GetComponent<MeshFilter>().sharedMesh.vertices), "Cloth seed changes shape");
            Check(Enumerable.Range(0, 50).Count(i => Mathf.Abs(table.ClothEdge(i * 3.1f, .7f, false).magnitude - table.ClothEdge(i * 3.1f + 180, .7f, false).magnitude) > .005f) > 35, "Opposite halves do not repeat the same pleat strip");
            table.Erase(345, 50); table.Erase(10, 50); table.Erase(91.25f, 27.5f);
            foreach (float a in new[] { 350f, 0f, 20f, 59f, 95f, 117f }) Check(!TopHit(table, a, out _), "Erased angle has no tabletop or collision: " + a);
            foreach (float a in new[] { 61f, 90f, 120f, 200f, 344f }) Check(TopHit(table, a, out _), "Uncut tabletop preserved at angle " + a);
            Check(table.VisibleIntervals().Count == 2, "Wrapped and overlapping gaps merge without extra seams");
            table.Erase(0, 360); Check(table.GetComponent<MeshFilter>().sharedMesh.vertexCount == 0 && collider.sharedMesh == null, "Erasing full ring clears geometry and collision");
            table.gaps.Clear(); table.innerRadius = float.NaN; table.outerRadius = -3; table.height = float.PositiveInfinity; table.Rebuild();
            Check(table.innerRadius >= .2f && table.outerRadius >= table.innerRadius + .249f && table.height > 0, "Invalid dimensions are normalized safely");
            table.innerRadius = 2.2f; table.outerRadius = 4.7f; table.height = 1.27f; table.clothSeed = 723; table.Erase(310, 85);
            Check(TopHit(table, 90, out top) && Mathf.Abs(table.transform.InverseTransformPoint(top.point).y - 1.27f) < .001f, "Resizing updates render mesh and collision height");
            PrefabUtility.RecordPrefabInstancePropertyModifications(table);
            var other = LevelPropPlacement.Place(layout, Prefab("DrapedRingTable"), new Pose(layout.World(new Vector2(10, 0), .01f), layout.transform.rotation)).GetComponent<DrapedRingTable>(); other.Rebuild();
            Check(other.GetComponent<MeshFilter>().sharedMesh != table.GetComponent<MeshFilter>().sharedMesh && other.gaps.Count == 0 && Mathf.Abs(other.outerRadius - 3.4f) < .001f, "Instances own independent parameters and meshes");
            Undo.IncrementCurrentGroup(); Undo.RecordObject(table, "Verify radius undo"); table.outerRadius = 5.3f; table.Rebuild(); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); await Task.Delay(200); table.Rebuild(); Check(Mathf.Abs(table.outerRadius - 4.7f) < .001f, "Resize Undo restores dimensions");
            Undo.PerformRedo(); await Task.Delay(200); table.Rebuild(); Check(Mathf.Abs(table.outerRadius - 5.3f) < .001f, "Resize Redo restores dimensions");
            table.outerRadius = 4.7f; table.Rebuild();
            designer = ScriptableObject.CreateInstance<LevelDesignerWindow>(); designer.Show(); designer.CreateGUI(); designer.Bind(layout);
            typeof(LevelDesignerWindow).GetField("selectedProp", Flags).SetValue(designer, table.gameObject);
            typeof(LevelDesignerWindow).GetMethod("Refresh", Flags).Invoke(designer, null);
            Check(designer.rootVisualElement.Q("ring-table-settings") != null, "Level designer exposes ring controls");
            Click(designer.rootVisualElement.Q<Button>("ring-erase-brush"));
            var sceneGUI = typeof(LevelDesignerWindow).GetMethod("RingTableSceneGUI", Flags);
            var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            Ray BrushRay(float a) { float r = (table.innerRadius + table.outerRadius) / 2; return new Ray(table.transform.TransformPoint(new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * r, 4, Mathf.Sin(a * Mathf.Deg2Rad) * r)), -table.transform.up); }
            var brushDone = new TaskCompletionSource<bool>();
            void BrushFrame(SceneView current)
            {
                if (current != view || Event.current.type != EventType.Repaint) return;
                SceneView.duringSceneGui -= BrushFrame;
                try
                {
                    foreach (var sample in new[] { (EventType.MouseDown, 130f), (EventType.MouseDrag, 158f), (EventType.MouseUp, 158f) })
                        sceneGUI.Invoke(designer, new object[] { view, BrushRay(sample.Item2), new Event { type = sample.Item1, button = 0 } });
                    brushDone.SetResult(true);
                }
                catch (Exception error) { brushDone.SetException(error); }
            }
            SceneView.duringSceneGui += BrushFrame; view.Show(); view.Repaint();
            try { if (await Task.WhenAny(brushDone.Task, Task.Delay(5000)) != brushDone.Task) throw new Exception("Scene view did not repaint for brush verification"); await brushDone.Task; }
            finally { SceneView.duringSceneGui -= BrushFrame; }
            Check(!TopHit(table, 145, out _), "Scene brush stroke removes an entire dragged interval");
            Undo.PerformUndo(); await Task.Delay(200); table.Rebuild(); Check(TopHit(table, 145, out _), "Single Undo restores entire brush stroke");
            sceneGUI.Invoke(designer, new object[] { view, BrushRay(0), new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape } });
            PrefabUtility.RecordPrefabInstancePropertyModifications(table);
            var stage = LevelPropPlacement.Place(layout, Prefab("RestaurantStage"), new Pose(layout.World(new Vector2(0, 13), .01f), layout.transform.rotation));
            var marker = stage.transform.Find("Elevator placement (deck level)");
            var elevator = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("LevelElevator"), scene); elevator.transform.SetPositionAndRotation(marker.position, stage.transform.rotation);
            var elevatorBounds = BoundsOf(Prefab("LevelElevator"));
            Check(elevatorBounds.size.x < 6 && elevatorBounds.size.z < 7 && elevatorBounds.size.y < 4.8f, "Measured elevator fits 6m by 7m central deck area with headroom");
            bool cornersSupported = true;
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
            {
                var p = elevatorBounds.center + Vector3.Scale(elevatorBounds.extents, new Vector3(x, 0, z)); p.y = 2;
                var ray = new Ray(elevator.transform.TransformPoint(p), -stage.transform.up);
                cornersSupported &= stage.GetComponents<Collider>().Any(c => c.Raycast(ray, out var h, 5) && Mathf.Abs(stage.transform.InverseTransformPoint(h.point).y - .6f) < .003f);
            }
            Check(cornersSupported, "Stage supports all four corners of actual elevator footprint");
            bool intersects = false;
            foreach (var a in elevator.GetComponentsInChildren<BoxCollider>()) foreach (var b in stage.GetComponents<BoxCollider>())
                if (!a.isTrigger && Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float depth) && depth > .002f) intersects = true;
            Check(!intersects, "Elevator does not overlap backdrop, returns or stairs");
            foreach (int side in new[] { -1, 1 }) for (int step = 0; step < 3; step++)
            {
                var ray = new Ray(stage.transform.TransformPoint(new Vector3(side * 5.05f, 2, -6.05f + step * .72f)), -stage.transform.up);
                var heights = stage.GetComponents<Collider>().Select(c => c.Raycast(ray, out var h, 5) ? stage.transform.InverseTransformPoint(h.point).y : -1).ToArray();
                Check(Mathf.Abs(heights.Max() - (step + 1) * .2f) < .005f, "Stair " + side + " ramp meets step " + step + " nosing at 20cm increments");
            }
            Object.DestroyImmediate(elevator);
            LevelBuilder.Persist(layout, source); EditorSceneManager.SaveScene(scene, source);
            Check(AssetDatabase.Contains(table.GetComponent<MeshFilter>().sharedMesh) && AssetDatabase.Contains(table.GetComponent<MeshCollider>().sharedMesh), "Customized render and collision meshes persist as assets");
            LevelBuilder.Export(layout, destination, false); exported = EditorSceneManager.OpenScene(destination, OpenSceneMode.Additive);
            var copied = exported.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DrapedRingTable>()).OrderByDescending(t => t.outerRadius).First(); copied.Rebuild();
            Check(Mathf.Abs(copied.outerRadius - 4.7f) < .001f && Mathf.Abs(copied.height - 1.27f) < .001f && copied.clothSeed == 723 && !TopHit(copied, 350, out _) && TopHit(copied, 90, out _), "Scene export/reopen retains size, seed, real gap and collision");
            LevelBuilder.ExportPrefab(layout, prefabPath, false);
            var layoutPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var savedTable = layoutPrefab.GetComponentsInChildren<DrapedRingTable>().OrderByDescending(t => t.outerRadius).First();
            Check(AssetDatabase.Contains(savedTable.GetComponent<MeshFilter>().sharedMesh) && savedTable.gaps.Count > 0, "Prefab export stores custom geometry and gap parameters");
            return new { passed = true, count = checks.Count, checks };
        }
        finally
        {
            if (designer != null) designer.Close();
            if (exported.IsValid() && exported.isLoaded) EditorSceneManager.CloseScene(exported, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            foreach (var path in new[] { source, destination, prefabPath })
            {
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                var folder = Path.ChangeExtension(path, null) + "_Geometry"; if (Directory.Exists(folder)) AssetDatabase.DeleteAsset(folder);
            }
            Selection.objects = selection;
            foreach (var window in windows) { typeof(LevelDesignerWindow).GetMethod("OnEnable", Flags).Invoke(window, null); typeof(LevelDesignerWindow).GetMethod("Refresh", Flags).Invoke(window, null); }
            Directory.CreateDirectory("Logs/RestaurantProps"); File.WriteAllLines("Logs/RestaurantProps/edit.txt", checks);
        }
    }
    public static object Preview()
    {
        Directory.CreateDirectory("Logs/RestaurantProps");
        var paths = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                var model = Object.Instantiate(Prefab(i < 2 ? "DrapedRingTable" : "RestaurantStage")); model.GetComponent<LevelProp>().enabled = false; preview.AddSingleGO(model);
                if (i < 2)
                {
                    var table = model.GetComponent<DrapedRingTable>();
                    if (i == 1) { table.Erase(215, 65); table.Erase(35, 30); table.height = 1.15f; }
                    table.Rebuild();
                }
                if (i == 3)
                {
                    var elevator = Object.Instantiate(Prefab("LevelElevator")); elevator.transform.SetParent(model.transform, false);
                    elevator.transform.localPosition = model.transform.Find("Elevator placement (deck level)").localPosition;
                }
                preview.lights[0].intensity = 2.6f; preview.lights[0].transform.rotation = Quaternion.Euler(45, 210, 0);
                preview.lights[1].intensity = 1.2f; preview.lights[1].transform.rotation = Quaternion.Euler(20, 80, 0);
                preview.ambientColor = new Color(.36f, .36f, .38f);
                var cam = preview.camera; cam.cameraType = CameraType.Preview; cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(.065f, .075f, .085f); cam.orthographic = true; cam.nearClipPlane = .05f; cam.farClipPlane = 100;
                var bounds = BoundsOf(model); var direction = i < 2 ? new Vector3(.7f, 1.25f, -2) : new Vector3(1, 1.25f, -2.2f);
                cam.orthographicSize = i < 2 ? 3.4f : 8.6f;
                cam.transform.position = bounds.center + direction.normalized * 35; cam.transform.LookAt(bounds.center);
                preview.BeginStaticPreview(new Rect(0, 0, 1200, 850)); preview.Render(true); var image = preview.EndStaticPreview();
                string path = "Logs/RestaurantProps/" + new[] { "ring-full", "ring-gaps", "stage", "stage-elevator" }[i] + ".png";
                File.WriteAllBytes(path, image.EncodeToPNG()); Object.DestroyImmediate(image); paths.Add(path);
            }
            finally { preview.Cleanup(); }
        }
        return paths;
    }
    public static async Task<object> Play()
    {
        checks.Clear(); Check(Application.isPlaying, "Runtime checks execute in Play Mode");
        int frame = Time.frameCount; await Task.Delay(150);
        Check(Time.frameCount > frame, "Player loop advances during verification");
        var root = new GameObject("Restaurant runtime verification"); root.transform.position = new Vector3(1500, 40, 1500);
        try
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(100, .2f, 100);
            var tableObject = Object.Instantiate(Prefab("DrapedRingTable"), root.transform); tableObject.transform.localPosition = new Vector3(-10, 0, 0);
            var table = tableObject.GetComponent<DrapedRingTable>();
            var stage = Object.Instantiate(Prefab("RestaurantStage"), root.transform); stage.transform.localPosition = new Vector3(12, 0, 0);
            var walker = new GameObject("Player-size controller", typeof(CharacterController)); walker.transform.SetParent(root.transform, false);
            var controller = walker.GetComponent<CharacterController>(); var player = Prefab("Player").GetComponent<CharacterController>();
            controller.height = player.height; controller.radius = player.radius; controller.center = player.center;
            controller.stepOffset = player.stepOffset; controller.slopeLimit = player.slopeLimit; controller.skinWidth = player.skinWidth;
            await Task.Delay(100); Physics.SyncTransforms();
            Check(table.GetComponent<MeshFilter>().sharedMesh != Prefab("DrapedRingTable").GetComponent<MeshFilter>().sharedMesh, "Runtime instance regenerates its own mesh");
            Check(table.GetComponent<Renderer>().sharedMaterial == table.GetComponent<LevelProp>().gameplayMaterial
                && stage.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m == stage.GetComponent<LevelProp>().gameplayMaterial)), "New props use existing black gameplay material");
            void Teleport(Vector3 position) { controller.enabled = false; walker.transform.position = position + Vector3.up * (controller.height * .5f - controller.center.y); controller.enabled = true; Physics.SyncTransforms(); }
            async Task Walk(Vector3 delta, int steps)
            {
                for (int i = 0; i < steps; i++) { controller.Move(delta + Vector3.down * .04f); await Task.Delay(15); }
            }
            Teleport(table.transform.TransformPoint(new Vector3(0, .03f, -4.8f)));
            await Walk(Vector3.forward * .05f, 100);
            Check(table.transform.InverseTransformPoint(walker.transform.position).z < -3.3f, "Intact ring blocks actual CharacterController passage");
            table.Erase(245, 50); await Task.Delay(50);
            Teleport(table.transform.TransformPoint(new Vector3(0, .03f, -4.8f))); await Walk(Vector3.forward * .05f, 100);
            Check(table.transform.InverseTransformPoint(walker.transform.position).z > -.5f, "CharacterController walks through erased gap into hollow center");
            table.height = 1.2f; await Task.Delay(100);
            Check(TopHit(table, 90, out var top) && Mathf.Abs(table.transform.InverseTransformPoint(top.point).y - 1.2f) < .003f, "Runtime dimension change rebuilds collision");
            Check(table.GetComponent<Renderer>().sharedMaterial == table.GetComponent<LevelProp>().gameplayMaterial, "Runtime rebuild preserves gameplay material");
            foreach (int side in new[] { -1, 1 })
            {
                Teleport(stage.transform.TransformPoint(new Vector3(side * 5.05f, .03f, -7.2f))); await Walk(Vector3.forward * .04f, 150);
                var local = stage.transform.InverseTransformPoint(walker.transform.position);
                Check(local.z > -2 && local.y + controller.center.y - controller.height * .5f > .54f, "Controller climbs " + side + " staircase onto stage: " + local);
                await Walk(Vector3.back * .04f, 150); local = stage.transform.InverseTransformPoint(walker.transform.position);
                Check(local.z < -6.8f && local.y + controller.center.y - controller.height * .5f < .15f, "Controller descends " + side + " staircase to floor");
            }
            return new { passed = true, count = checks.Count, controller = new { controller.radius, controller.height, controller.stepOffset }, checks };
        }
        finally
        {
            Object.Destroy(root); Directory.CreateDirectory("Logs/RestaurantProps"); File.WriteAllLines("Logs/RestaurantProps/play.txt", checks);
        }
    }
}
