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
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class VerifyRedVineArea
{
    static readonly List<string> checks = new();
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RedVineArea.prefab");
    static void Check(bool pass, string label) { checks.Add((pass ? "PASS: " : "FAIL: ") + label); if (!pass) throw new Exception(label); }
    static Vector4[] Points(MonsterVines vines) { var data = new Vector4[MonsterVines.Capacity]; int count = vines.Collect(data); return data.Take(count).ToArray(); }
    static void Report(string mode) { Directory.CreateDirectory("Logs/RedVineArea"); File.WriteAllLines("Logs/RedVineArea/" + mode + ".txt", checks); }
    public static async Task<object> Edit()
    {
        checks.Clear(); Check(!Application.isPlaying, "Editor checks outside Play Mode");
        var original = SceneManager.GetActiveScene(); var selection = Selection.objects;
        var windows = Resources.FindObjectsOfTypeAll<LevelDesignerWindow>();
        foreach (var window in windows) typeof(LevelDesignerWindow).GetMethod("OnDisable", Flags).Invoke(window, null);
        Scene scene = default, exported = default; LevelDesignerWindow designer = null;
        string id = Guid.NewGuid().ToString("N"), path = "Assets/Scenes/__VineArea_" + id + ".unity", asset = "Assets/Scenes/__VineArea_" + id + ".prefab";
        string source = "Assets/Scenes/__VineAreaSource_" + id + ".unity";
        try
        {
            Check(PropCatalog.All().Contains(Prefab) && PropCatalog.Category(Prefab) == "通用" && PropCatalog.Name(Prefab) == "红色藤蔓区域", "Named prop is available in General category");
            Check(Prefab.GetComponent<MonsterVines>() != null && Prefab.GetComponent<PatrolMonster>() == null && Prefab.GetComponent<MonsterScanEmitter>() == null, "Only vines, without monster AI or red scans");
            Check(Prefab.GetComponentsInChildren<Collider>(true).Length == 0, "Effect prop adds no collision");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Vine area verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.position = new Vector3(600, 0, 600); layout.transform.rotation = Quaternion.Euler(0, 33, 0);
            layout.rooms.Add(new LevelRoom { size = Vector2.one * 30 }); LevelBuilder.Rebuild(layout);
            Check(LevelPropPlacement.TryPose(layout, Prefab, new Ray(layout.World(Vector2.zero, 5), -layout.transform.up), PropSurface.Floor, .01f, 0, out var pose), "Area snaps to rotated layout floor");
            var instance = LevelPropPlacement.Place(layout, Prefab, pose); var area = instance.GetComponent<MonsterVineArea>();
            var serialized = new SerializedObject(area); serialized.FindProperty("width").floatValue = 8.25f; serialized.FindProperty("depth").floatValue = 3.75f; serialized.FindProperty("height").floatValue = 2.6f; serialized.ApplyModifiedProperties();
            await Task.Delay(200);
            Check(area.ContainsWorld(instance.transform.TransformPoint(new Vector3(4.12f, 2.59f, 1.87f)))
                && !area.ContainsWorld(instance.transform.TransformPoint(new Vector3(4.14f, 1, 0)))
                && !area.ContainsWorld(instance.transform.TransformPoint(new Vector3(0, 1, 1.89f)))
                && !area.ContainsWorld(instance.transform.TransformPoint(new Vector3(0, 2.62f, 0))), "Rotated rectangular clipping follows width, depth and height");
            Check(instance.transform.Find("Editor preview (hidden in Play)").gameObject.activeSelf, "Authoring range preview is visible");
            for (int i = 0; i < 8; i++) Check(area.WorldBounds.Contains(instance.transform.TransformPoint(new Vector3((i & 1) == 0 ? -4.125f : 4.125f, (i & 2) == 0 ? 0 : 2.6f, (i & 4) == 0 ? -1.875f : 1.875f))), "Culling bounds include rotated corner " + i);
            Undo.IncrementCurrentGroup(); Undo.RecordObject(area, "Resize vine rectangle"); area.width = 12; area.RefreshPreview(); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); await Task.Delay(150); Check(Mathf.Abs(area.width - 8.25f) < .001f, "Width Undo restores previous rectangle");
            Undo.PerformRedo(); await Task.Delay(150); Check(Mathf.Abs(area.width - 12) < .001f, "Width Redo restores edited rectangle");
            area.width = 8.25f; area.RefreshPreview(); PrefabUtility.RecordPrefabInstancePropertyModifications(area);
            designer = ScriptableObject.CreateInstance<LevelDesignerWindow>(); designer.Show(); designer.CreateGUI(); designer.Bind(layout);
            typeof(LevelDesignerWindow).GetField("selectedProp", Flags).SetValue(designer, instance); typeof(LevelDesignerWindow).GetMethod("Refresh", Flags).Invoke(designer, null);
            Check(designer.rootVisualElement.Query<Foldout>().ToList().Any(f => f.text == "红色藤蔓矩形范围"), "Level editor exposes rectangle settings");
            LevelBuilder.Persist(layout, source); EditorSceneManager.SaveScene(scene, source);
            LevelBuilder.Export(layout, path, false); exported = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var copy = exported.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonsterVineArea>()).Single();
            Check(copy.Size == area.Size && PrefabUtility.IsPartOfPrefabInstance(copy.gameObject), "Scene export preserves dimensions and prefab reference");
            LevelBuilder.ExportPrefab(layout, asset, false);
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(asset).GetComponentInChildren<MonsterVineArea>().Size == area.Size, "Layout prefab export preserves dimensions");
            return new { passed = true, count = checks.Count, checks };
        }
        finally
        {
            if (designer != null) designer.Close(); if (exported.IsValid() && exported.isLoaded) EditorSceneManager.CloseScene(exported, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original); if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            foreach (var item in new[] { path, asset, source }) { if (File.Exists(item)) AssetDatabase.DeleteAsset(item); var folder = Path.ChangeExtension(item, null) + "_Geometry"; if (Directory.Exists(folder)) AssetDatabase.DeleteAsset(folder); }
            Selection.objects = selection;
            foreach (var window in windows) { typeof(LevelDesignerWindow).GetMethod("OnEnable", Flags).Invoke(window, null); typeof(LevelDesignerWindow).GetMethod("Refresh", Flags).Invoke(window, null); }
            Report("edit");
        }
    }
    static async Task<Color32[]> Capture(Camera camera, string name)
    {
        await Task.Delay(200); var previous = RenderTexture.active; var image = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = camera.targetTexture; image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
            Directory.CreateDirectory("Logs/RedVineArea"); File.WriteAllBytes("Logs/RedVineArea/" + name + ".png", image.EncodeToPNG()); return image.GetPixels32();
        }
        finally { RenderTexture.active = previous; Object.Destroy(image); }
    }
    static int Red(Color32[] pixels) => pixels.Count(p => p.r > 15 && p.g < 5 && p.b < 5);
    public static async Task<object> Play()
    {
        checks.Clear(); Check(Application.isPlaying, "Runtime verification in Play Mode");
        var menu = Object.FindAnyObjectByType<SonicSpaceStartMenu>(); if (menu != null && SonicSpaceStartMenu.BlocksGameplay) menu.BeginGame();
        for (int i = 0; i < 60 && SonicSpaceStartMenu.BlocksGameplay; i++) await Task.Delay(100);
        Check(!SonicSpaceStartMenu.BlocksGameplay, "Gameplay is unpaused");
        int frame = Time.frameCount; await Task.Delay(100); Check(Time.frameCount > frame, "Player loop advances");
        var fixture = new GameObject("Red vine area runtime verification"); fixture.transform.position = new Vector3(100, 100, 100);
        RenderTexture target = null; MonsterVines vines = null;
        try
        {
            var black = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            GameObject Box(string name, Vector3 local, Vector3 size, Quaternion rotation)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(fixture.transform, false);
                go.transform.localPosition = local; go.transform.localRotation = rotation; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = black; return go;
            }
            Box("Floor", new Vector3(0, -.1f, 0), new Vector3(24, .2f, 24), Quaternion.identity);
            var instance = Object.Instantiate(Prefab, fixture.transform); instance.transform.localPosition = Vector3.zero; instance.transform.localRotation = Quaternion.Euler(0, 27, 0);
            var area = instance.GetComponent<MonsterVineArea>(); area.width = 8; area.depth = 4; area.height = 2.5f; area.RefreshPreview();
            vines = instance.GetComponent<MonsterVines>(); vines.lifetime = 3.6f;
            var wall = Box("Wall", fixture.transform.InverseTransformPoint(area.transform.TransformPoint(new Vector3(0, 1.5f, 1.8f))), new Vector3(12, 3, .2f), instance.transform.localRotation);
            var ordinary = new GameObject("Original monster style").AddComponent<MonsterVines>(); ordinary.transform.SetParent(fixture.transform, false);
            ordinary.transform.localPosition = new Vector3(-7, 0, -6);
            var cameraObject = new GameObject("Vine verification camera", typeof(Camera)); cameraObject.transform.SetParent(fixture.transform, false);
            var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.nearClipPlane = .05f; camera.farClipPlane = 35; camera.fieldOfView = 55;
            camera.transform.position = area.transform.TransformPoint(new Vector3(0, 5, -9)); camera.transform.LookAt(area.transform.TransformPoint(new Vector3(0, .6f, 0)));
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            target = new RenderTexture(960, 600, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            Physics.SyncTransforms(); await Task.Delay(2300);
            var points = Points(vines);
            Check(points.Length > 80 && points.Length <= MonsterVines.Capacity, "Rectangle grows bounded branching point population: " + points.Length);
            Check(points.All(p => area.ContainsWorld(p)), "Every point stays inside rotated rectangle and height");
            Check(points.Count(p => Mathf.Abs(p.y - 100.025f) < .04f) > 30, "Vines attach to actual floor");
            Check(points.Any(p => area.transform.InverseTransformPoint(p).y > .2f), "Vines climb real walls inside rectangle");
            Check(points.All(p => Mathf.Abs(p.y - 100.025f) < .04f || Mathf.Abs(Mathf.Abs(wall.transform.InverseTransformPoint(p).z) - .5f) < .3f), "Samples remain on floor or wall rather than floating");
            Check(Points(ordinary).Length > 30 && ordinary.WorldBounds.size == Vector3.one * 10, "Original non-area monster vine path still grows"); ordinary.enabled = false;
            Check(!instance.transform.Find("Editor preview (hidden in Play)").gameObject.activeSelf && instance.GetComponentsInChildren<Collider>(true).Length == 0, "Preview is hidden in Play; prop creates no blocking geometry");
            var visible = await Capture(camera, "rectangle-vines"); Check(Red(visible) > 60, "Actual render graph draws red vines: " + Red(visible));
            PlayerUiPointMask.SetLegacyPanel(vines, new Rect(0, 0, 16384, 16384), true);
            // UI masks intentionally exclude RenderTextures; verify the screen-camera path separately.
            var zones = new Vector4[PlayerUiPointMask.Capacity]; var options = new Vector4[PlayerUiPointMask.Capacity];
            Check(PlayerUiPointMask.Collect(camera, zones, options) == 0, "Offscreen capture preserves existing UI-mask exclusion");
            camera.targetTexture = null;
            Check(PlayerUiPointMask.Collect(camera, zones, options) > 0, "Screen-camera UI mask accepts effect owner");
            camera.targetTexture = target; PlayerUiPointMask.SetLegacyPanel(vines, default, false);
            var occluder = Box("Occluder", fixture.transform.InverseTransformPoint(camera.transform.position + camera.transform.forward), new Vector3(20, 20, .1f), camera.transform.rotation);
            var hidden = await Capture(camera, "occluded"); Check(Red(hidden) == 0, "World geometry occludes area vines"); Object.Destroy(occluder);
            area.width = 3; area.depth = 2; Check(Points(vines).Length == 0, "Resize immediately suppresses stale out-of-date trail");
            await Task.Delay(900); Check(Points(vines).All(p => area.ContainsWorld(p)), "New growth respects resized bounds");
            instance.transform.position += Vector3.up * 20; Check(Points(vines).Length == 0, "Moving effect immediately clears visible old trail");
            await Task.Delay(800); Check(Points(vines).Length == 0, "Unsupported region produces no floating vines"); instance.transform.position -= Vector3.up * 20;
            Object.Destroy(wall); area.width = 16; area.depth = 6; instance.transform.rotation = Quaternion.identity; area.RefreshPreview();
            camera.orthographic = true; camera.orthographicSize = .9f;
            camera.transform.position = area.transform.TransformPoint(new Vector3(7, 10, 0)); camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            await Task.Delay(2200);
            Check(Points(vines).Any(p => area.transform.InverseTransformPoint(p).x > 5.5f), "Large rectangle grows beyond old monster distance limit");
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            Check(!GeometryUtility.TestPlanesAABB(planes, new Bounds(instance.transform.position, Vector3.one * 10)) && GeometryUtility.TestPlanesAABB(planes, vines.WorldBounds), "Wide-area culling uses rectangle rather than monster bounds");
            var edge = await Capture(camera, "wide-edge"); Check(Red(edge) > 8, "GPU renders rectangle edge while center is offscreen: " + Red(edge));
            vines.surfaces = 0; await Task.Delay(4500); Check(Points(vines).Length == 0, "Trails expire when supported growth stops");
            vines.enabled = false; Check(!MonsterVines.Instances.Contains(vines) && vines.PointCount == 0, "Disable removes effect from render registry and clears points");
            Check(!ShaderUtil.ShaderHasError(Shader.Find("Sound2/Screen Grid Face And Corner Points")), "Point shader remains valid");
            return new { passed = true, count = checks.Count, checks };
        }
        finally
        {
            if (vines != null) PlayerUiPointMask.SetLegacyPanel(vines, default, false);
            foreach (var camera in fixture.GetComponentsInChildren<Camera>()) { camera.enabled = false; camera.targetTexture = null; }
            Object.Destroy(fixture); if (target != null) { target.Release(); Object.Destroy(target); } Report("play");
        }
    }
}
