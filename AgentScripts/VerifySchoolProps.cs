using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class VerifySchoolProps
{
    static readonly string[] Names = { "SchoolBlackboard", "SchoolDesk", "SchoolChair", "SchoolChalkBox", "SchoolSpeaker" };
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
    static void Check(bool pass, string label) { checks.Add((pass ? "PASS: " : "FAIL: ") + label); if (!pass) throw new Exception(label); }
    static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", Flags).Invoke(button.clickable, new object[] { null });
    static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
    }
    public static object Edit()
    {
        checks.Clear(); Check(!Application.isPlaying, "Editor is outside Play Mode");
        var original = SceneManager.GetActiveScene(); var selection = Selection.objects;
        var windows = Resources.FindObjectsOfTypeAll<LevelDesignerWindow>();
        foreach (var w in windows) typeof(LevelDesignerWindow).GetMethod("OnDisable", Flags).Invoke(w, null);
        Scene scene = default, exported = default; PropBrowserWindow browser = null;
        string id = Guid.NewGuid().ToString("N");
        string source = "Assets/Scenes/__SchoolSource_" + id + ".unity", destination = "Assets/Scenes/__SchoolExport_" + id + ".unity";
        try
        {
            var all = PropCatalog.All(); Check(all.Count == 56 && all.Distinct().Count() == 56, "Catalog contains 56 distinct props");
            int[] counts = { 11, 5, 4, 4, 1, 2, 3, 26 };
            for (int i = 0; i < counts.Length; i++) Check(all.Count(p => PropCatalog.Category(p) == PropCatalog.Categories[i]) == counts[i], PropCatalog.Categories[i] + " category count " + counts[i]);
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var layout = new GameObject("School verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.position = new Vector3(700, 0, 700); layout.transform.rotation = Quaternion.Euler(0, 27, 0);
            layout.rooms.Add(new LevelRoom { size = Vector2.one * 30 }); LevelBuilder.Rebuild(layout);
            GameObject desk = null;
            foreach (string name in Names)
            {
                var prefab = Prefab(name); Check(prefab != null, name + " exists");
                var metadata = prefab.GetComponent<LevelProp>(); bool wall = name == "SchoolBlackboard" || name == "SchoolSpeaker";
                Check(metadata != null && metadata.gameplayMaterial != null && metadata.surface == (wall ? PropSurface.Wall : PropSurface.Floor), name + " has correct surface and gameplay material");
                var mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
                Check(mesh != null && AssetDatabase.Contains(mesh) && prefab.GetComponentsInChildren<MeshRenderer>().Length == 1 && prefab.GetComponents<Collider>().Length > 0, name + " uses one persistent combined mesh and compound collision");
                Check(prefab.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m != null)) && mesh.subMeshCount == prefab.GetComponentInChildren<Renderer>().sharedMaterials.Length, name + " has valid material slots");
                var bounds = BoundsOf(prefab);
                Check(wall ? bounds.min.z >= -.001f : Mathf.Abs(bounds.min.y) < .002f, name + " anchor touches back or bottom without clipping");
                Ray ray = wall ? new Ray(layout.World(new Vector2(-6 + Array.IndexOf(Names, name) * 2, 0), 2), layout.transform.forward)
                    : new Ray(layout.World(new Vector2(-5 + Array.IndexOf(Names, name) * 2, 0), 3), -layout.transform.up);
                Check(LevelPropPlacement.TryPose(layout, prefab, ray, metadata.surface, .01f, 0, out var pose), name + " snaps in rotated layout");
                var instance = LevelPropPlacement.Place(layout, prefab, pose);
                Check(PrefabUtility.IsPartOfPrefabInstance(instance), name + " placement keeps prefab link");
                if (name == "SchoolDesk") desk = instance;
            }
            Check(LevelPropPlacement.TryPose(layout, Prefab("SchoolChalkBox"), new Ray(desk.transform.TransformPoint(new Vector3(0, 2, 0)), -desk.transform.up), PropSurface.Floor, .01f, 0, out var tablePose)
                && Mathf.Abs(desk.transform.InverseTransformPoint(tablePose.position).y - .7675f) < .003f, "Chalk box snaps to actual desktop height");
            LevelBuilder.Persist(layout, source); EditorSceneManager.SaveScene(scene, source);
            LevelBuilder.Export(layout, destination, false); exported = EditorSceneManager.OpenScene(destination, OpenSceneMode.Additive);
            var copies = exported.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelProp>()).ToArray();
            Check(copies.Length == 5 && copies.All(p => PrefabUtility.IsPartOfPrefabInstance(p.gameObject)), "Save/export/reopen retains all five school props and prefab links");
            browser = ScriptableObject.CreateInstance<PropBrowserWindow>(); browser.Show(); browser.CreateGUI();
            var ui = browser.rootVisualElement;
            Check(ui.Query<Button>(className: "prop-category").ToList().Count == 9, "Browser offers All plus eight category filters");
            foreach (var category in PropCatalog.Categories)
            {
                Click(ui.Q<Button>("prop-category-" + category));
                var cards = ui.Query<Button>(className: "prop-card").ToList();
                Check(cards.Count == all.Count(p => PropCatalog.Category(p) == category)
                    && cards.All(b => PropCatalog.Category(Prefab(b.name.Substring("prop-card-".Length))) == category), category + " button filters actual cards");
            }
            Click(ui.Q<Button>("prop-category-学校"));
            var search = ui.Q<ToolbarSearchField>("prop-search"); search.value = "课";
            Check(ui.Query<Button>(className: "prop-card").ToList().Count == 2, "Chinese search intersects school category");
            search.value = " schoolspeaker "; Check(ui.Query<Button>(className: "prop-card").ToList().Count == 1, "English search ignores case and outer spaces");
            GameObject chosen = null; typeof(PropBrowserWindow).GetField("choose", Flags).SetValue(browser, (Action<GameObject>)(p => chosen = p));
            Click(ui.Q<Button>("prop-card-SchoolSpeaker")); Check(chosen == Prefab("SchoolSpeaker"), "Filtered card invokes selection callback with correct prefab");
            search.value = "购物车"; Check(ui.Query<Button>(className: "prop-card").ToList().Count == 0, "Empty search result is scoped to category");
            Click(ui.Q<Button>("prop-category-全部")); Check(ui.Query<Button>(className: "prop-card").ToList().Count == 1, "All restores cross-category search");
            search.value = ""; Check(ui.Query<Button>(className: "prop-card").ToList().Count == 56 && ui.Query<Label>(className: "prop-group-title").ToList().Count == 8, "All shows 56 cards in eight groups");
            return new { passed = true, count = checks.Count, checks };
        }
        finally
        {
            if (browser != null) browser.Close();
            if (exported.IsValid() && exported.isLoaded) EditorSceneManager.CloseScene(exported, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            foreach (var path in new[] { source, destination })
            {
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                var geometry = Path.ChangeExtension(path, null) + "_Geometry"; if (Directory.Exists(geometry)) AssetDatabase.DeleteAsset(geometry);
            }
            Selection.objects = selection;
            foreach (var w in windows) { typeof(LevelDesignerWindow).GetMethod("OnEnable", Flags).Invoke(w, null); typeof(LevelDesignerWindow).GetMethod("Refresh", Flags).Invoke(w, null); }
            Directory.CreateDirectory("Logs/SchoolProps"); File.WriteAllLines("Logs/SchoolProps/edit.txt", checks);
        }
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
                    var prefab = Prefab(i == 5 ? "Chair" : Names[i]); var model = Object.Instantiate(prefab);
                    model.GetComponent<LevelProp>().enabled = false; preview.AddSingleGO(model);
                    preview.lights[0].intensity = 2.5f; preview.lights[0].transform.rotation = Quaternion.Euler(45, 210, 0);
                    preview.lights[1].intensity = .75f; preview.lights[1].transform.rotation = Quaternion.Euler(20, 110, 0);
                    var cam = preview.camera; cam.cameraType = CameraType.Preview; cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(.055f, .07f, .09f); cam.orthographic = true; cam.nearClipPlane = .01f; cam.farClipPlane = 50;
                    var bounds = BoundsOf(model); float extent = Mathf.Max(bounds.size.x / (640f / 520), bounds.size.y, bounds.size.z);
                    cam.orthographicSize = Mathf.Max(.04f, extent * .75f);
                    var direction = i == 0 ? new Vector3(.35f, .2f, 2.1f) : i == 4 ? new Vector3(.7f, .5f, 2.1f) : new Vector3(.85f, 1.2f, -2.1f);
                    cam.transform.position = bounds.center + direction.normalized * 10; cam.transform.LookAt(bounds.center);
                    preview.BeginStaticPreview(new Rect(0, 0, 640, 520)); preview.Render(true); var frame = preview.EndStaticPreview();
                    atlas.SetPixels(i % 3 * 640, (1 - i / 3) * 520, 640, 520, frame.GetPixels()); Object.DestroyImmediate(frame);
                }
                finally { preview.Cleanup(); }
            }
            atlas.Apply(); Directory.CreateDirectory("Logs/SchoolProps"); File.WriteAllBytes("Logs/SchoolProps/models.png", atlas.EncodeToPNG());
            return "Logs/SchoolProps/models.png";
        }
        finally { Object.DestroyImmediate(atlas); }
    }
    public static object OpenBrowser()
    {
        var designer = EditorWindow.GetWindow<LevelDesignerWindow>(); designer.Show();
        var open = designer.rootVisualElement.Q<Button>("open-prop-browser");
        if (open != null) Click(open);
        else PropBrowserWindow.Open(prefab => { if (designer != null) typeof(LevelDesignerWindow).GetMethod("ChooseProp", Flags).Invoke(designer, new object[] { prefab }); });
        var browser = EditorWindow.GetWindow<PropBrowserWindow>(); browser.position = new Rect(150, 120, 860, 680);
        if (browser.rootVisualElement.Q("prop-search") == null) browser.CreateGUI();
        browser.rootVisualElement.Q<ToolbarSearchField>("prop-search").value = "";
        Click(browser.rootVisualElement.Q<Button>("prop-category-学校")); return "School category opened";
    }
}
