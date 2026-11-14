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
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class VerifyPropReposition
{
    static readonly List<string> checks = new();
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool passed, string message)
    {
        checks.Add((passed ? "PASS: " : "FAIL: ") + message);
        if (!passed) throw new Exception(message);
    }
    static object Call(LevelDesignerWindow window, string method, params object[] args) =>
        typeof(LevelDesignerWindow).GetMethod(method, Private).Invoke(window, args);
    static object Field(LevelDesignerWindow window, string name) => typeof(LevelDesignerWindow).GetField(name, Private).GetValue(window);
    static void Set(LevelDesignerWindow window, string name, object value) => typeof(LevelDesignerWindow).GetField(name, Private).SetValue(window, value);
    static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .002f;

    static async Task Dispatch(LevelDesignerWindow window, SceneView view, Ray ray, Event evt)
    {
        bool done = false;
        Exception error = null;
        void Draw(SceneView current)
        {
            if (current != view || done || Event.current.type != EventType.Repaint) return;
            done = true;
            try
            {
                checks.Add($"EVENT: {evt.type}, alt={evt.alt}, surface={Field(window, "repositionSurface")}");
                Call(window, "PropSceneGUI", current, ray, evt);
                checks.Add($"RESULT: {Field(window, "status")}");
            }
            catch (Exception e) { error = e; }
        }
        SceneView.duringSceneGui += Draw;
        try
        {
            for (int i = 0; i < 60 && !done; i++) { view.Repaint(); await Task.Delay(100); }
            if (!done) throw new Exception("Scene GUI did not repaint");
            if (error != null) throw error;
        }
        finally { SceneView.duringSceneGui -= Draw; }
    }

    public static async Task<object> Run()
    {
        checks.Clear();
        Check(!Application.isPlaying, "Verification runs in Edit Mode");
        var originalScene = SceneManager.GetActiveScene();
        var originalSelection = Selection.objects;
        var existingWindows = Resources.FindObjectsOfTypeAll<LevelDesignerWindow>();
        foreach (var existing in existingWindows) Call(existing, "OnDisable");
        var sceneView = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
        var pivot = sceneView.pivot; var rotation = sceneView.rotation; var size = sceneView.size; var ortho = sceneView.orthographic;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var window = ScriptableObject.CreateInstance<LevelDesignerWindow>();
        var exportPath = "Assets/Scenes/__PropRepositionVerification.unity";
        var exportFolder = "Assets/Scenes/__PropRepositionVerification_Geometry";
        var sourcePath = "Assets/Scenes/__PropRepositionSource.unity";
        var sourceFolder = "Assets/Scenes/__PropRepositionSource_Geometry";
        bool ownsArtifacts = false;
        Scene exported = default;
        try
        {
            Check(!File.Exists(exportPath) && !Directory.Exists(exportFolder) && !File.Exists(sourcePath) && !Directory.Exists(sourceFolder), "Temporary export destinations are unused");
            ownsArtifacts = true;
            SceneManager.SetActiveScene(scene);
            var layout = new GameObject("Reposition verification", typeof(LevelLayout)).GetComponent<LevelLayout>();
            layout.transform.position = new Vector3(400, 0, 400);
            layout.transform.rotation = Quaternion.Euler(0, 27, 0);
            layout.rooms.Add(new LevelRoom { name = "验证房间", size = new Vector2(12, 12) });
            LevelBuilder.Rebuild(layout);
            window.titleContent = new GUIContent("重新吸附验证");
            window.position = new Rect(150, 100, 900, 780); window.Show();
            Set(window, "layout", layout); Call(window, "Refresh");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TickingClock.prefab");
            var initialRay = new Ray(layout.World(Vector2.zero, 1.8f), layout.transform.forward);
            Check(LevelPropPlacement.TryPose(layout, prefab, initialRay, PropSurface.Wall, .01f, 17, out var initial), "New clock still snaps to a rotated wall");
            var instance = LevelPropPlacement.Place(layout, prefab, initial);
            instance.name = "保留参数的时钟";
            instance.transform.localScale = new Vector3(1.2f, .8f, 1.1f);
            var clock = instance.GetComponent<TickingClock>();
            clock.tickInterval = .37f; clock.running = false; clock.initialLongStep = 7;
            PrefabUtility.RecordPrefabInstancePropertyModifications(clock);
            var metadata = instance.GetComponent<LevelProp>();
            metadata.surfaceAnchor = new Vector3(.1f, -.05f, .02f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(metadata);
            var blocker = new GameObject("Own child collider", typeof(BoxCollider));
            blocker.transform.SetParent(instance.transform, false);
            blocker.GetComponent<BoxCollider>().size = Vector3.one * 1.4f;
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            var oldPosition = instance.transform.position; var oldRotation = instance.transform.rotation;
            var oldScale = instance.transform.localScale;
            var componentJson = instance.GetComponents<Component>().Where(c => !(c is Transform)).ToDictionary(c => c, EditorJsonUtility.ToJson);
            int count = LevelPropPlacement.PropsRoot(layout).childCount;
            Call(window, "BeginReposition", instance);
            Check(Field(window, "tool").ToString() == "RepositionProp", "Existing instance enters reposition mode");
            Check(Mathf.Abs((float)Field(window, "repositionAngle") - 17) < .1f, "Reposition initializes the existing wall roll");
            Check(window.rootVisualElement.Q("geometry-objects") != null && window.rootVisualElement.Q("prop-objects") != null, "Hierarchy separates geometry from props");
            Check(window.rootVisualElement.Q("prop-objects").Query<Button>().ToList().Any(b => b.text.Contains(instance.name)), "Prop is selectable in the prop group");
            Check(window.rootVisualElement.Q("properties").Query<InspectorElement>().ToList().Count == 0, "Map properties do not embed full component inspectors");
            Check(Near(oldPosition, instance.transform.position), "Entering preview leaves original position untouched");
            Check(LevelPropPlacement.TryPose(layout, instance, initialRay, PropSurface.Wall, .01f, 17, out var throughSelf), "Ray through own child collider still reaches the wall");
            var anchorWorld = throughSelf.position + throughSelf.rotation * Vector3.Scale(metadata.surfaceAnchor, instance.transform.lossyScale);
            Check(Mathf.Abs(layout.transform.InverseTransformPoint(anchorWorld).z - 5.99f) < .005f, "Self and child colliders are excluded from surface snapping");
            var newRay = new Ray(layout.World(new Vector2(0, -1), 2.1f), layout.transform.right);
            Check(LevelPropPlacement.TryPose(layout, instance, newRay, PropSurface.Wall, metadata.surfaceOffset, 17, out var expected), "Existing instance can snap to a different wall");
            await Dispatch(window, sceneView, newRay, new Event { type = EventType.MouseDown, button = 0, alt = true });
            Check(Near(oldPosition, instance.transform.position) && Field(window, "tool").ToString() == "RepositionProp", "Alt navigation does not commit reposition");
            await Dispatch(window, sceneView, new Ray(layout.World(Vector2.zero, 2), -layout.transform.up), new Event { type = EventType.MouseDown, button = 0 });
            Check(Near(oldPosition, instance.transform.position) && Field(window, "tool").ToString() == "RepositionProp", "Invalid floor target does not move a wall clock or exit preview");
            await Dispatch(window, sceneView, newRay, new Event { type = EventType.MouseDown, button = 0 });
            Check(Near(expected.position, instance.transform.position) && Quaternion.Angle(expected.rotation, instance.transform.rotation) < .1f,
                $"Scene left click commits expected surface pose (distance {Vector3.Distance(expected.position, instance.transform.position)}, angle {Quaternion.Angle(expected.rotation, instance.transform.rotation)}, tool {Field(window, "tool")})");
            Check(Field(window, "tool").ToString() == "Select", "Successful reposition returns to selection mode");
            Check(LevelPropPlacement.PropsRoot(layout).childCount == count && instance.GetComponent<TickingClock>() == clock, "Reposition moves the same object without duplication");
            Check(instance.name == "保留参数的时钟" && Near(instance.transform.localScale, oldScale), "Name and nonuniform instance scale are preserved");
            Check(componentJson.All(pair => EditorJsonUtility.ToJson(pair.Key) == pair.Value), "All serialized component parameters and references are unchanged");
            Check(PrefabUtility.IsPartOfPrefabInstance(instance), "Original Prefab connection is preserved");
            Undo.PerformUndo();
            Check(Near(oldPosition, instance.transform.position) && Quaternion.Angle(oldRotation, instance.transform.rotation) < .05f, "One Undo restores original position and rotation");
            Undo.PerformRedo();
            Check(Near(expected.position, instance.transform.position) && Quaternion.Angle(expected.rotation, instance.transform.rotation) < .05f, "One Redo restores the new surface pose");
            Check(componentJson.All(pair => EditorJsonUtility.ToJson(pair.Key) == pair.Value), "Undo and Redo retain all component parameters");
            Call(window, "BeginReposition", instance);
            await Dispatch(window, sceneView, initialRay, new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
            Check(Near(expected.position, instance.transform.position) && Field(window, "tool").ToString() == "Select", "Esc cancels preview without moving the original");
            Undo.PerformUndo();
            Check(Near(oldPosition, instance.transform.position), "Cancelled preview adds no extra Undo step");
            Undo.PerformRedo();
            var ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab");
            var floorRay = new Ray(layout.World(new Vector2(2, 2), 4), -layout.transform.up);
            Check(LevelPropPlacement.TryPose(layout, ballPrefab, floorRay, PropSurface.Floor, .02f, 0, out var floorPose), "Floor placement remains available");
            var ball = LevelPropPlacement.Place(layout, ballPrefab, floorPose);
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "验证台面"; table.transform.SetParent(LevelPropPlacement.PropsRoot(layout), false);
            table.transform.localPosition = new Vector3(-2, .6f, 0); table.transform.localScale = new Vector3(2, 1.2f, 2);
            var tableRay = new Ray(layout.World(new Vector2(-2, 0), 4), -layout.transform.up);
            Check(LevelPropPlacement.TryPose(layout, ball, tableRay, PropSurface.Floor, .02f, 0, out var tablePose), "Existing floor prop snaps onto another prop's top");
            LevelPropPlacement.Reposition(layout, ball, tablePose);
            Check(Mathf.Abs(layout.transform.InverseTransformPoint(ball.transform.TransformPoint(ball.GetComponent<LevelProp>().surfaceAnchor)).y - 1.22f) < .005f, "Floor prop bottom anchor lands on the tabletop with the requested gap");
            Call(window, "BeginReposition", instance); Call(window, "Select", Enum.Parse(typeof(LevelDesignerWindow).GetNestedType("Kind", BindingFlags.NonPublic), "Room"), 0);
            Check(Field(window, "tool").ToString() == "Select" && Field(window, "selectedProp") == null, "Selecting geometry cancels reposition and switches inspector");
            Check(window.rootVisualElement.Q("properties").Query<FloatField>().ToList().Count > 0, "Room parameters appear in the right inspector");
            Call(window, "BeginReposition", instance); Call(window, "CancelReposition");
            await Task.Delay(250);
            var left = window.rootVisualElement.Q("hierarchy-pane").worldBound;
            var right = window.rootVisualElement.Q("inspector-pane").worldBound;
            Check(left.width >= 190 && right.width >= 300 && right.xMin >= left.xMax - 1 && Mathf.Abs(left.yMin - right.yMin) < 1, "Hierarchy and Inspector render side by side with usable widths");
            window.position = new Rect(150, 100, 600, 440);
            window.rootVisualElement.Q<Foldout>("settings-foldout").value = true;
            await Task.Delay(250);
            var workspace = window.rootVisualElement.Q("workspace").worldBound;
            var footer = window.rootVisualElement.Q("footer").worldBound;
            Check(workspace.height >= 99 && footer.yMax <= window.rootVisualElement.worldBound.yMax + 1, "Small window keeps both editing panes and footer reachable with settings expanded");
            LevelBuilder.Persist(layout, sourcePath); EditorSceneManager.SaveScene(scene, sourcePath);
            LevelBuilder.Export(layout, exportPath, false);
            exported = EditorSceneManager.OpenScene(exportPath, OpenSceneMode.Additive);
            var copy = exported.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TickingClock>(true)).Single();
            Check(Near(copy.transform.position, expected.position) && Mathf.Abs(copy.tickInterval - .37f) < .0001f && !copy.running && copy.initialLongStep == 7, "Export and reopen retain repositioned pose and clock tuning");
            Check(PrefabUtility.IsPartOfPrefabInstance(copy.gameObject), "Export preserves clock Prefab connection");
            return new { passed = true, checks };
        }
        finally
        {
            window.Close();
            if (exported.IsValid() && exported.isLoaded) EditorSceneManager.CloseScene(exported, true);
            if (originalScene.IsValid()) SceneManager.SetActiveScene(originalScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (ownsArtifacts)
            {
                if (File.Exists(exportPath)) AssetDatabase.DeleteAsset(exportPath);
                if (Directory.Exists(exportFolder)) AssetDatabase.DeleteAsset(exportFolder);
                if (File.Exists(sourcePath)) AssetDatabase.DeleteAsset(sourcePath);
                if (Directory.Exists(sourceFolder)) AssetDatabase.DeleteAsset(sourceFolder);
            }
            Selection.objects = originalSelection;
            foreach (var existing in existingWindows) { Call(existing, "OnEnable"); Call(existing, "Refresh"); }
            sceneView.LookAt(pivot, rotation, size, ortho, true);
            Directory.CreateDirectory("Logs/PropReposition");
            File.WriteAllLines("Logs/PropReposition/edit.txt", checks);
        }
    }
}
