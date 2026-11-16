using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Object = UnityEngine.Object;

public static class VerifyWorldLevelEditing
{
    const string Folder = "Assets/__VerifyWorldEditing";
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    public static object VerifyBed()
    {
        checks = 0;
        var original = SceneManager.GetActiveScene();
        var selection = Selection.activeGameObject;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var window = ScriptableObject.CreateInstance<LevelDesignerWindow>();
        try
        {
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Bed placement test", typeof(LevelLayout));
            root.transform.position = new Vector3(9000, 0, 9000);
            var level = root.GetComponent<LevelLayout>(); level.manualGeometry = true;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = Vector3.down * .5f; floor.transform.localScale = new Vector3(10,1,10);
            Selection.activeGameObject = root;
            window.Bind(null); // Reproduce a stale target after the selection event already fired.
            var bed = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Bed.prefab");
            typeof(LevelDesignerWindow).GetMethod("ChooseProp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[]{bed});
            Check((LevelLayout)typeof(LevelDesignerWindow).GetField("layout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window) == level,
                "Choosing bed rebinds the selected level despite stale window state");
            Check(LevelPropPlacement.TryPose(level, bed, new Ray(root.transform.position+Vector3.up*3,Vector3.down),PropSurface.Floor,.01f,0,out var pose), "Bed snaps to authored floor");
            Check(!LevelPropOverlap.HasOverlap(level,bed,pose), "Bed feet clear the floor");
            var placed = LevelPropPlacement.Place(level,bed,pose);
            Check(placed.transform.parent == root.transform.Find("Props"), "Bed is placed under selected level");
            Check(placed.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).SequenceEqual(
                LevelPropOptimizer.ForPlacement(bed).GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials)), "Bed keeps original materials");
            return new {passed=true,checks};
        }
        finally
        {
            Object.DestroyImmediate(window);
            EditorSceneManager.CloseScene(scene,true);
            SceneManager.SetActiveScene(original);
            Selection.activeGameObject = selection;
        }
    }
    public static object PrepareVisual()
    {
        var view = SceneView.lastActiveSceneView;
        SessionState.SetString("WorldEditingVisual.Scene", SceneManager.GetActiveScene().path);
        SessionState.SetVector3("WorldEditingVisual.Pivot", view.pivot);
        SessionState.SetVector3("WorldEditingVisual.Rotation", view.rotation.eulerAngles);
        SessionState.SetFloat("WorldEditingVisual.Size", view.size);
        SessionState.SetBool("WorldEditingVisual.Ortho", view.orthographic);
        SessionState.SetBool("WorldEditingVisual.Lighting", view.sceneLighting);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        scene.name = "WorldEditingVisual"; SceneManager.SetActiveScene(scene);
        var root = new GameObject("Preview test", typeof(LevelLayout));
        root.transform.position = new Vector3(20000, 0, 20000);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.1f,.8f,.25f), hideFlags = HideFlags.DontSave };
        GameObject Cube(string name, Vector3 position, Vector3 scale, bool prop)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(root.transform, false);
            g.transform.localPosition = position; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = material;
            if (prop) g.AddComponent<LevelProp>();
            return g;
        }
        Cube("Prop", new Vector3(-1.2f,1,0), new Vector3(1,2,1), true);
        Cube("Scene wall same material", new Vector3(1.2f,1,0), new Vector3(1,2,1), false);
        Cube("Floor", new Vector3(0,-.1f,0), new Vector3(6,.2f,4), false);
        Cube("Occluder", new Vector3(-1.2f,.45f,-.7f), new Vector3(1.5f,.9f,.2f), false);
        var window = EditorWindow.GetWindow<LevelDesignerWindow>("关卡编辑器"); window.Bind(root.GetComponent<LevelLayout>());
        view.sceneLighting = true;
        view.LookAt(root.transform.position + Vector3.up, Quaternion.identity, 4, true, true); view.Focus(); view.Repaint();
        return new { scene = scene.name };
    }
    public static object PreviewOff()
    {
        var window = EditorWindow.GetWindow<LevelDesignerWindow>();
        typeof(LevelDesignerWindow).GetField("propColorPreview", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, false);
        SceneView.RepaintAll(); return true;
    }
    public static object CleanupVisual()
    {
        var scene = SceneManager.GetSceneByName("WorldEditingVisual");
        var material = scene.GetRootGameObjects()[0].GetComponentInChildren<Renderer>().sharedMaterial;
        EditorSceneManager.CloseScene(scene, true); Object.DestroyImmediate(material);
        var original = SceneManager.GetSceneByPath(SessionState.GetString("WorldEditingVisual.Scene", ""));
        SceneManager.SetActiveScene(original);
        var window = EditorWindow.GetWindow<LevelDesignerWindow>();
        typeof(LevelDesignerWindow).GetField("propColorPreview", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, true);
        var level = original.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LevelLayout>(true))
            .First(l => l.name.Contains("超市"));
        window.Bind(level);
        var view = SceneView.lastActiveSceneView;
        view.sceneLighting = SessionState.GetBool("WorldEditingVisual.Lighting", true);
        view.LookAt(SessionState.GetVector3("WorldEditingVisual.Pivot", Vector3.zero),
            Quaternion.Euler(SessionState.GetVector3("WorldEditingVisual.Rotation", Vector3.zero)),
            SessionState.GetFloat("WorldEditingVisual.Size", 10), SessionState.GetBool("WorldEditingVisual.Ortho", true), true);
        return new { original.path, original.isDirty, view.sceneLighting };
    }
    public static object Run()
    {
        if (Application.isPlaying || PrefabStageUtility.GetCurrentPrefabStage() != null || Directory.Exists(Folder))
            throw new Exception("Requires main stage in Edit Mode and unused test folder.");
        checks = 0;
        var original = SceneManager.GetActiveScene();
        bool dirty = original.isDirty;
        var view = SceneView.lastActiveSceneView;
        bool lighting = view.sceneLighting;
        var mode = view.cameraMode;
        var pivot = view.pivot; var rotation = view.rotation; float size = view.size; bool ortho = view.orthographic;
        var window = ScriptableObject.CreateInstance<LevelDesignerWindow>();
        window.CreateGUI();
        Scene scene = default;
        try
        {
            var levels = original.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LevelLayout>(true)).ToArray();
            foreach (var level in levels)
            {
                bool active = level.gameObject.activeSelf;
                window.Bind(level);
                Check(PrefabStageUtility.GetCurrentPrefabStage() == null, "Scene instance stays in main stage");
                Check(window.rootVisualElement.Q("tools").enabledSelf, "Scene prop tools enabled");
                Check(level.gameObject.activeSelf == active, "Binding preserves activation");
            }
            Check(levels.Length == 8, "All eight SoundAndShell levels discovered");
            Check(view.sceneLighting == lighting && view.cameraMode.Equals(mode), "Binding preserves Scene lighting and draw mode");
            Check(original.isDirty == dirty, "Binding does not dirty world scene");

            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Authored level", typeof(LevelLayout));
            var authored = root.GetComponent<LevelLayout>(); authored.manualGeometry = true;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Authored floor";
            floor.transform.SetParent(root.transform, false); floor.transform.localScale = new Vector3(20, 1, 20);
            floor.transform.localPosition = new Vector3(0, -.5f, 0);
            var assetPath = Folder + "/Level.prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            Object.DestroyImmediate(root);
            var bytes = File.ReadAllBytes(assetPath);
            root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            root.transform.position = new Vector3(9000, 0, 9000);
            var levelInstance = root.GetComponent<LevelLayout>();
            window.Bind(levelInstance);
            var chair = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Chair.prefab");
            Check(levelInstance.generatedRoot == null, "Authored level has no Generated Geometry");
            Check(LevelPropPlacement.TryPose(levelInstance, chair, new Ray(root.transform.position + Vector3.up * 5, Vector3.down),
                PropSurface.Floor, .01f, 0, out var pose), "Placement finds authored floor");
            var placed = LevelPropPlacement.Place(levelInstance, chair, pose);
            Check(LevelPropOverlap.HasOverlap(levelInstance, chair, new Pose(pose.position - Vector3.up * .5f, pose.rotation)),
                "Authored floor blocks overlapping placement");
            string placedName = placed.name;
            Check(placed.transform.parent == root.transform.Find("Props"), "Placement creates Props under prefab instance");
            Check(PrefabUtility.IsPartOfPrefabInstance(placed), "Placed prop retains prefab link");
            var materials = placed.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToArray();
            var expected = LevelPropOptimizer.ForPlacement(chair).GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToArray();
            Check(materials.SequenceEqual(expected), "Placement preserves original material slots");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(placed == null, "Undo removes placed prop");
            Undo.PerformRedo();
            placed = root.transform.Find("Props").Find(placedName).gameObject;
            Check(placed != null, "Redo restores placed prop");
            var moved = new Pose(pose.position + Vector3.right * 3, pose.rotation);
            LevelPropPlacement.Reposition(levelInstance, placed, moved);
            Check(Vector3.Distance(placed.transform.position, moved.position) < .001f, "Reposition scene prop");
            Undo.PerformUndo();
            Check(Vector3.Distance(placed.transform.position, pose.position) < .001f, "Undo reposition");
            bool rejected = false;
            try { LevelPropPlacement.Place(levelInstance, AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WoodenDoor.prefab"), pose); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Embedded door rejects before wall mutations");
            string scenePath = Folder + "/World.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.CloseScene(scene, true);
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            root = scene.GetRootGameObjects().Single();
            placed = root.transform.Find("Props").Find(placedName).gameObject;
            Check(placed.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).SequenceEqual(expected), "Save/reload retains original materials");
            Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == assetPath, "Save/reload retains level prefab link");
            Check(bytes.SequenceEqual(File.ReadAllBytes(assetPath)), "Scene editing leaves source prefab unchanged");
            Check(root.GetComponent<LevelLayout>().generatedRoot == null, "No generated geometry created");
            Check(view.sceneLighting == lighting && view.cameraMode.Equals(mode), "All editing leaves scene appearance settings unchanged");
            var report = new { passed = true, checks, levels = levels.Select(l => l.name).ToArray() };
            Directory.CreateDirectory("Logs/WorldLevelEditing");
            File.WriteAllText("Logs/WorldLevelEditing/results.json", Newtonsoft.Json.JsonConvert.SerializeObject(report));
            return report;
        }
        finally
        {
            Object.DestroyImmediate(window);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(original);
            AssetDatabase.DeleteAsset(Folder);
            view.LookAt(pivot, rotation, size, ortho, true);
        }
    }
}
