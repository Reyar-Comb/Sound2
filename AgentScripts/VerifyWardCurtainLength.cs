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

public static class VerifyWardCurtainLength
{
    static readonly List<string> checks = new();
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WardCurtain.prefab");
    static void Check(bool pass, string label) { checks.Add((pass ? "PASS: " : "FAIL: ") + label); if (!pass) throw new Exception(label); }
    static Vector3[] Slot(Mesh mesh, int slot) { var vertices = mesh.vertices; return mesh.GetTriangles(slot).Distinct().Select(i => vertices[i]).ToArray(); }
    public static async Task<object> Edit()
    {
        checks.Clear(); Check(!Application.isPlaying, "Verification outside Play Mode");
        var original = SceneManager.GetActiveScene(); var selection = Selection.objects;
        var windows = Resources.FindObjectsOfTypeAll<LevelDesignerWindow>();
        foreach (var window in windows) typeof(LevelDesignerWindow).GetMethod("OnDisable", Flags).Invoke(window, null);
        Scene scene = default, exported = default; LevelDesignerWindow designer = null;
        string id = Guid.NewGuid().ToString("N");
        string source = "Assets/Scenes/__CurtainSource_" + id + ".unity", destination = "Assets/Scenes/__CurtainExport_" + id + ".unity", exportPrefab = "Assets/Scenes/__CurtainLayout_" + id + ".prefab";
        try
        {
            Check(AssetDatabase.AssetPathToGUID("Assets/Prefabs/WardCurtain.prefab") == "714f3ca19051c3841b7ed246f681d8c8", "Original prefab GUID preserved");
            Check(PropCatalog.Category(Prefab) == "医院", "Curtain remains in Hospital category");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Curtain verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.position = new Vector3(900, 0, 900); layout.transform.rotation = Quaternion.Euler(0, 31, 0);
            layout.rooms.Add(new LevelRoom { size = Vector2.one * 30 }); LevelBuilder.Rebuild(layout);
            var instance = LevelPropPlacement.Place(layout, Prefab, new Pose(layout.World(Vector2.zero, .01f), layout.transform.rotation));
            var curtain = instance.GetComponent<ResizableWardCurtain>(); Check(curtain != null, "Prefab instance inherits length component");
            var collision = instance.GetComponent<BoxCollider>();
            foreach (float length in new[] { .73f, 1.37f, 2.5f, 4.83f, 8.36f })
            {
                var serialized = new SerializedObject(curtain); serialized.FindProperty("length").floatValue = length; serialized.ApplyModifiedProperties();
                await Task.Delay(180); Physics.SyncTransforms();
                var mesh = curtain.Model.sharedMesh; var cloth = Slot(mesh, 2); var wheels = Slot(mesh, 1); var steel = Slot(mesh, 0);
                Check(Mathf.Abs(cloth.Max(v => v.x) - cloth.Min(v => v.x) - (length - .14f)) < .0001f && instance.transform.localScale == Vector3.one && curtain.Model.transform.localScale == Vector3.one, "Exact unscaled cloth length at " + length + "; span=" + (cloth.Max(v => v.x) - cloth.Min(v => v.x)) + "; scales=" + instance.transform.localScale + "/" + curtain.Model.transform.localScale);
                Check(cloth.All(v => Mathf.Abs(v.y - .35f) < .0001f || Mathf.Abs(v.y - 2.23f) < .0001f) && cloth.All(v => Mathf.Abs(v.z - ResizableWardCurtain.FoldZ(v.x + length * .5f - .07f)) < .0001f), "Constant physical fold wavelength, depth and height at " + length);
                var xs = cloth.Select(v => v.x).Distinct().OrderBy(x => x).ToArray();
                Check(Enumerable.Range(1, xs.Length - 2).All(i => Mathf.Abs(xs[i] - xs[i - 1] - .059f) < .0001f) && xs[xs.Length - 1] - xs[xs.Length - 2] <= .0591f, "Length changes add folds and trim only final panel at " + length);
                var leftWheels = wheels.Where(v => v.x < 0).ToArray();
                Check(Mathf.Abs(leftWheels.Max(v => v.x) - leftWheels.Min(v => v.x) - .1f) < .0001f
                    && Mathf.Abs(steel.Where(v => v.y > 2.29f && v.y < 2.35f).Max(v => v.x) - length * .5f - .02f) < .0001f, "Wheel diameter and post thickness unchanged at " + length);
                Check(Mathf.Abs(collision.size.x - length) < .0001f && collision.size.y == 2.05f && collision.size.z == .16f
                    && collision.Raycast(new Ray(instance.transform.TransformPoint(new Vector3(length * .5f - .02f, 1, -1)), instance.transform.forward), out _, 3), "Collision extends to adjusted edge at " + length);
                Check(curtain.Model.GetComponent<Renderer>().sharedMaterials.Length == 3 && mesh.subMeshCount == 3, "All material slots survive rebuild at " + length);
            }
            curtain.length = 3.47f; curtain.Rebuild();
            Undo.IncrementCurrentGroup(); Undo.RecordObject(curtain, "Resize ward curtain"); curtain.length = 6.19f; curtain.Rebuild(); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); await Task.Delay(180); Check(Mathf.Abs(curtain.length - 3.47f) < .001f && Mathf.Abs(collision.size.x - 3.47f) < .001f, "Undo restores length and collision");
            Undo.PerformRedo(); await Task.Delay(180); Check(Mathf.Abs(curtain.length - 6.19f) < .001f && Mathf.Abs(collision.size.x - 6.19f) < .001f, "Redo restores length and collision");
            var second = LevelPropPlacement.Place(layout, Prefab, new Pose(layout.World(new Vector2(0, 5), .01f), layout.transform.rotation)).GetComponent<ResizableWardCurtain>(); second.Rebuild();
            Check(second.length == 2.5f && second.Model.sharedMesh != curtain.Model.sharedMesh, "Instances resize independently");
            designer = ScriptableObject.CreateInstance<LevelDesignerWindow>(); designer.Show(); designer.CreateGUI(); designer.Bind(layout);
            typeof(LevelDesignerWindow).GetField("selectedProp", Flags).SetValue(designer, instance);
            typeof(LevelDesignerWindow).GetMethod("Refresh", Flags).Invoke(designer, null);
            Check(UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Foldout>(designer.rootVisualElement).ToList().Any(f => f.text == "病房隔帘尺寸（保持褶皱尺度）"), "Level designer exposes curtain length section");
            PrefabUtility.RecordPrefabInstancePropertyModifications(curtain);
            LevelBuilder.Persist(layout, source); EditorSceneManager.SaveScene(scene, source);
            Check(AssetDatabase.Contains(curtain.Model.sharedMesh) && curtain.Model.sharedMesh.subMeshCount == 3, "Customized geometry persists with three material slots");
            LevelBuilder.Export(layout, destination, false); exported = EditorSceneManager.OpenScene(destination, OpenSceneMode.Additive);
            var copied = exported.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ResizableWardCurtain>()).OrderByDescending(c => c.length).First();
            await Task.Delay(180);
            Check(Mathf.Abs(copied.length - 6.19f) < .001f && Mathf.Abs(copied.GetComponent<BoxCollider>().size.x - 6.19f) < .001f && copied.Model.sharedMesh.subMeshCount == 3, "Export/reopen retains length, shape, collision and materials");
            LevelBuilder.ExportPrefab(layout, exportPrefab, false);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(exportPrefab).GetComponentsInChildren<ResizableWardCurtain>().OrderByDescending(c => c.length).First();
            Check(Mathf.Abs(saved.length - 6.19f) < .001f && AssetDatabase.Contains(saved.Model.sharedMesh), "Layout prefab export retains custom geometry");
            return new { passed = true, count = checks.Count, checks };
        }
        finally
        {
            if (designer != null) designer.Close();
            if (exported.IsValid() && exported.isLoaded) EditorSceneManager.CloseScene(exported, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            foreach (var path in new[] { source, destination, exportPrefab })
            { if (File.Exists(path)) AssetDatabase.DeleteAsset(path); var folder = Path.ChangeExtension(path, null) + "_Geometry"; if (Directory.Exists(folder)) AssetDatabase.DeleteAsset(folder); }
            Selection.objects = selection;
            foreach (var window in windows) { typeof(LevelDesignerWindow).GetMethod("OnEnable", Flags).Invoke(window, null); typeof(LevelDesignerWindow).GetMethod("Refresh", Flags).Invoke(window, null); }
            Directory.CreateDirectory("Logs/WardCurtainLength"); File.WriteAllLines("Logs/WardCurtainLength/edit.txt", checks);
        }
    }
    public static object Preview()
    {
        var atlas = new Texture2D(1800, 600, TextureFormat.RGB24, false); var lengths = new[] { 1.2f, 2.5f, 5.3f };
        try
        {
            for (int i = 0; i < lengths.Length; i++)
            {
                var preview = new PreviewRenderUtility();
                try
                {
                    var model = Object.Instantiate(Prefab); preview.AddSingleGO(model); model.GetComponent<LevelProp>().enabled = false;
                    var curtain = model.GetComponent<ResizableWardCurtain>(); curtain.length = lengths[i]; curtain.Rebuild();
                    preview.lights[0].intensity = 2.5f; preview.lights[0].transform.rotation = Quaternion.Euler(45, 210, 0);
                    preview.lights[1].intensity = 1; preview.lights[1].transform.rotation = Quaternion.Euler(20, 80, 0);
                    var cam = preview.camera; cam.cameraType = CameraType.Preview; cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(.06f, .075f, .09f); cam.orthographic = true; cam.orthographicSize = 3.1f; cam.nearClipPlane = .01f; cam.farClipPlane = 50;
                    var target = new Vector3(0, 1.2f, 0); cam.transform.position = target + new Vector3(.6f, .4f, -3).normalized * 15; cam.transform.LookAt(target);
                    preview.BeginStaticPreview(new Rect(0, 0, 600, 600)); preview.Render(true); var frame = preview.EndStaticPreview();
                    atlas.SetPixels(i * 600, 0, 600, 600, frame.GetPixels()); Object.DestroyImmediate(frame);
                }
                finally { preview.Cleanup(); }
            }
            atlas.Apply(); Directory.CreateDirectory("Logs/WardCurtainLength"); File.WriteAllBytes("Logs/WardCurtainLength/lengths.png", atlas.EncodeToPNG()); return "Logs/WardCurtainLength/lengths.png";
        }
        finally { Object.DestroyImmediate(atlas); }
    }
}
